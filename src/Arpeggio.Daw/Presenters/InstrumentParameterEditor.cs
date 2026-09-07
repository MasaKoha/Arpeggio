using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>保存形式に実在する音色項目とパネルの入力を相互変換する。</summary>
    internal static class InstrumentParameterEditor
    {
        internal static IReadOnlyList<InstrumentParameter> Describe(Instrument instrument)
        {
            JsonObject source = ReadObject(instrument);
            List<InstrumentParameter> parameters = new List<InstrumentParameter>();
            foreach (KeyValuePair<string, JsonNode?> property in source)
            {
                if (property.Key is "id" or "name" or "kind" or "sampleData")
                {
                    continue;
                }
                if (property.Key == "envelope")
                {
                    foreach (KeyValuePair<string, JsonNode?> envelopeProperty in property.Value!.AsObject())
                    {
                        parameters.Add(DescribeValue($"envelope.{envelopeProperty.Key}", envelopeProperty.Value));
                    }
                    continue;
                }
                parameters.Add(DescribeValue(property.Key, property.Value));
            }
            return parameters;
        }

        internal static Instrument Apply(Instrument instrument, string name, IReadOnlyDictionary<string, string> values)
        {
            JsonObject source = ReadObject(instrument);
            source["name"] = name;
            foreach (InstrumentParameter parameter in Describe(instrument))
            {
                if (!values.TryGetValue(parameter.Key, out string? text))
                {
                    continue;
                }
                string[] path = parameter.Key.Split('.');
                JsonObject target = path.Length == 1 ? source : source[path[0]]!.AsObject();
                string key = path[^1];
                target[key] = ParseValue(key, text, target[key]);
            }
            return InstrumentJson.Deserialize(source.ToJsonString());
        }

        private static JsonObject ReadObject(Instrument instrument) => JsonNode.Parse(InstrumentJson.Serialize(instrument))!.AsObject();

        private static InstrumentParameter DescribeValue(string key, JsonNode? value)
        {
            string text;
            if (key.EndsWith("Macro", StringComparison.Ordinal))
            {
                text = FormatMacro(value);
            }
            else if (value is JsonArray array)
            {
                text = string.Join(",", array.Select(sample => sample!.ToString()));
            }
            else if (value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                text = value!.GetValue<bool>() ? "true" : "false";
            }
            else
            {
                text = value?.ToString() ?? string.Empty;
            }
            string label = key == "waveform" && value is not JsonArray ? "波形" : Label(key);
            return new InstrumentParameter { Key = key, Label = label, Value = text, Choices = Choices(key, value) };
        }

        private static string FormatMacro(JsonNode? value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            string values = string.Join(",", value["values"]!.AsArray().Select(sample => sample!.ToString()));
            int loopIndex = value["loopIndex"]!.GetValue<int>();
            return loopIndex < 0 ? values : $"{values}/{loopIndex}";
        }

        private static JsonNode? ParseValueWithoutTemplate(string key, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (key == "adsrRegisters")
            {
                int[] registers = InstrumentMacroText.ParseValues(text);
                if (registers.Length != 4)
                {
                    throw new ArgumentException("ADSR レジスタは attack,decay,sustainLevel,sustainRate の 4 値で指定してください。");
                }
                return new JsonObject
                {
                    ["attack"] = registers[0], ["decay"] = registers[1], ["sustainLevel"] = registers[2], ["sustainRate"] = registers[3]
                };
            }
            if (bool.TryParse(text, out bool flag))
            {
                return JsonValue.Create(flag);
            }
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
            {
                return JsonValue.Create(integer);
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
            {
                return JsonValue.Create(number);
            }
            return JsonValue.Create(text);
        }

        private static JsonNode? ParseValue(string key, string text, JsonNode? previous)
        {
            if (key.EndsWith("Macro", StringComparison.Ordinal))
            {
                Macro? macro = InstrumentMacroText.Parse(text);
                return macro == null ? null : new JsonObject
                {
                    ["values"] = JsonSerializer.SerializeToNode(macro.Values), ["loopIndex"] = macro.LoopIndex
                };
            }
            if (previous is JsonArray)
            {
                return JsonSerializer.SerializeToNode(InstrumentMacroText.ParseValues(text));
            }
            if (previous is null)
            {
                // null で保存されている任意項目（adsrRegisters 等）は型の手掛かりが無いので、キーと入力文字列から決める
                return ParseValueWithoutTemplate(key, text);
            }
            JsonValueKind kind = previous.GetValueKind();
            if (kind == JsonValueKind.String)
            {
                return JsonValue.Create(text);
            }
            if (kind is JsonValueKind.True or JsonValueKind.False)
            {
                return JsonValue.Create(bool.Parse(text));
            }
            if (key is "initialVolume" or "envelopeStepFrames" or "outputLevel" or "lfsrWidth" or
                "sampleRate" or "rootMidiNote" or "loopStart" or "loopEnd")
            {
                return JsonValue.Create(int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            {
                throw new ArgumentException($"{Label(key)} は有限の数値で指定してください。");
            }
            return JsonValue.Create(number);
        }

        private static string[] Choices(string key, JsonNode? value) => key switch
        {
            "duty" => Enum.GetNames<DutyCycle>().Where(name => name != nameof(DutyCycle.None)).ToArray(),
            "noiseMode" => new[] { nameof(NoiseMode.Long), nameof(NoiseMode.Short) },
            "waveform" when value is not JsonArray => Enum.GetNames<SnesWaveformKind>()
                .Where(name => name != nameof(SnesWaveformKind.None)).ToArray(),
            "loop" or "envelopeIncreasing" => new[] { "true", "false" },
            "outputLevel" => new[] { "0", "25", "50", "100" },
            "lfsrWidth" => new[] { "7", "15" },
            _ => Array.Empty<string>()
        };

        private static string Label(string key) => key switch
        {
            "duty" => "デューティ比",
            "noiseMode" => "ノイズ周期",
            "volumeMacro" => "音量マクロ（0〜15）",
            "arpeggioMacro" => "アルペジオマクロ（半音）",
            "pitchMacro" => "ピッチマクロ",
            "dutyMacro" => "デューティマクロ（1〜4）",
            "initialVolume" => "初期音量（0〜15）",
            "envelopeIncreasing" => "エンベロープ増加",
            "envelopeStepFrames" => "音量変化間隔（フレーム、0＝無効）",
            "waveform" => "波形（GB：32 要素、各 0〜15）",
            "outputLevel" => "出力音量（%）",
            "lfsrWidth" => "LFSR ビット幅",
            "loop" => "波形ループ",
            "envelope.attackSeconds" => "Attack（秒）",
            "envelope.decaySeconds" => "Decay（秒）",
            "envelope.sustainLevel" => "Sustain（0〜1）",
            "envelope.releaseSeconds" => "Release（秒）",
            "echoSend" => "エコー送り（0〜1）",
            "pan" => "定位（-1〜1）",
            _ => key
        };
    }
}
