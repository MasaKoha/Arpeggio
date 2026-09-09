using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>音色の任意引数と kind ごとの JSON プロパティを対応付ける。</summary>
    internal sealed class InstrumentOptions
    {
        private const int EnvelopeValueCount = 4;
        private readonly List<(Option<string?> Option, string PropertyName, Func<string, JsonNode?> Parse)> bindings = new();
        private readonly Option<string?> preset = new("--preset") { Description = "SNES 内蔵音色名。推奨値も再適用" };
        private readonly Option<string?> waveform = new("--waveform") { Description = "SNES: Sine/Square/Saw/Triangle/Pulse/Noise、GB Wave: 32 個の 0〜15 のカンマ区切り" };

        internal InstrumentOptions(Command command)
        {
            command.Options.Add(Kind);
            command.Options.Add(preset);
            Add(command, "--name", "name", "音色名", value => JsonValue.Create(value));
            Add(command, "--duty", "duty", "12.5 / 25 / 50 / 75", ParseDuty);
            Add(command, "--volume-macro", "volumeMacro", "0〜15 の値列[/loopIndex]。null で解除", ParseMacro);
            Add(command, "--arpeggio-macro", "arpeggioMacro", "半音の値列[/loopIndex]。null で解除", ParseMacro);
            Add(command, "--pitch-macro", "pitchMacro", "セントの値列[/loopIndex]。null で解除", ParseMacro);
            Add(command, "--duty-macro", "dutyMacro", "DutyCycle 整数値 1〜4 の値列[/loopIndex]。null で解除", ParseMacro);
            Add(command, "--noise-mode", "noiseMode", "NES Noise: Long / Short", value => ParseEnum<NoiseMode>(value));
            Add(command, "--initial-volume", "initialVolume", "GB Pulse: 初期音量 0〜15", ParseInteger);
            Add(command, "--envelope-increasing", "envelopeIncreasing", "GB Pulse: true / false", ParseBoolean);
            Add(command, "--envelope-step-frames", "envelopeStepFrames", "GB Pulse: 60 Hz のフレーム数。0 で無効", ParseInteger);
            Add(command, "--output-level", "outputLevel", "GB Wave: 0 / 25 / 50 / 100", ParseInteger);
            Add(command, "--lfsr-width", "lfsrWidth", "GB Noise: 7 / 15", ParseInteger);
            Add(command, "--loop", "loop", "SNES: true / false", ParseBoolean);
            Add(command, "--adsr", "envelope", "SNES: attack秒,decay秒,sustain(0〜1),release秒", ParseEnvelope);
            Add(command, "--adsr-registers", "adsrRegisters", "SNES: attack,decay,sustainLevel,sustainRate", ParseRegisters);
            Add(command, "--root", "rootMidiNote", "SNES: 元の MIDI 音程または音名", value => JsonValue.Create(Arpeggio.Core.Document.NoteName.Parse(value))!);
            Add(command, "--echo-send", "echoSend", "SNES: エコー送り 0〜1", ParseDouble);
            Add(command, "--pan", "pan", "SNES: 左右定位 -1〜1", ParseDouble);
            command.Options.Add(waveform);
        }

        internal Option<InstrumentKind?> Kind { get; } = new("--kind") { Description = "NesPulse / NesTriangle / NesNoise / NesDpcm / GbPulse / GbWave / GbNoise / SnesSample" };

        internal void RequireNewInstrument()
        {
            Kind.Required = true;
            bindings.Find(binding => binding.PropertyName == "name").Option.Required = true;
        }

        internal Instrument Apply(ParseResult result, Instrument instrument)
        {
            Instrument replacement = instrument;
            string? selectedPreset = result.GetValue(preset);
            if (selectedPreset != null)
            {
                replacement = InstrumentJson.Deserialize(InstrumentJson.Serialize(instrument));
                if (replacement is not SnesSampleInstrument sample)
                {
                    throw new ArgumentException("--preset は SnesSample にだけ指定できます。");
                }
                sample.ApplyPreset(selectedPreset);
            }
            JsonObject document = CreateEditableDocument(replacement);
            foreach ((Option<string?> option, string propertyName, Func<string, JsonNode?> parse) in bindings)
            {
                string? value = result.GetValue(option);
                if (value is null)
                {
                    continue;
                }
                RequireProperty(document, propertyName, option.Name);
                document[propertyName] = parse(value);
                if (propertyName == "envelope")
                {
                    document["adsrRegisters"] = null;
                }
            }
            ApplyWaveform(result, document);
            return InstrumentJson.Deserialize(document.ToJsonString());
        }

        internal static Instrument Create(InstrumentKind kind)
        {
            return kind switch
            {
                InstrumentKind.NesPulse => new NesPulseInstrument(),
                InstrumentKind.NesTriangle => new NesTriangleInstrument(),
                InstrumentKind.NesNoise => new NesNoiseInstrument(),
                InstrumentKind.NesDpcm => new NesDpcmInstrument(),
                InstrumentKind.GbPulse => new GbPulseInstrument(),
                InstrumentKind.GbWave => new GbWaveInstrument(),
                InstrumentKind.GbNoise => new GbNoiseInstrument(),
                InstrumentKind.SnesSample => new SnesSampleInstrument(),
                _ => throw new ArgumentException("有効な音色 kind を指定してください。")
            };
        }

        internal static Instrument ChangeKind(Instrument instrument, InstrumentKind kind)
        {
            if (instrument.Kind == kind)
            {
                return instrument;
            }
            JsonObject previous = ParseDocument(InstrumentJson.Serialize(instrument));
            JsonObject replacement = CreateEditableDocument(Create(kind));
            foreach (KeyValuePair<string, JsonNode?> property in previous)
            {
                if (property.Key == "kind" || !replacement.ContainsKey(property.Key))
                {
                    continue;
                }
                // GB と SNES の waveform は同名でも配列と enum で意味が異なる。
                if (property.Key == "waveform")
                {
                    continue;
                }
                replacement[property.Key] = property.Value?.DeepClone();
            }
            return InstrumentJson.Deserialize(replacement.ToJsonString());
        }

        private void Add(Command command, string name, string propertyName, string description, Func<string, JsonNode?> parse)
        {
            Option<string?> option = new(name) { Description = description };
            command.Options.Add(option);
            bindings.Add((option, propertyName, parse));
        }

        private void ApplyWaveform(ParseResult result, JsonObject document)
        {
            string? value = result.GetValue(waveform);
            if (value is null)
            {
                return;
            }
            RequireProperty(document, "waveform", waveform.Name);
            document["waveform"] = document["waveform"] is JsonArray
                ? JsonSerializer.SerializeToNode(MacroOption.ParseValues(value))
                : ParseEnum<SnesWaveformKind>(value);
        }

        private static JsonObject ParseDocument(string json)
        {
            return JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("音色 JSON はオブジェクトです。");
        }

        private static JsonObject CreateEditableDocument(Instrument instrument)
        {
            JsonObject document = ParseDocument(InstrumentJson.Serialize(instrument));
            // 保存時に省略する optional マクロも、未設定から指定・音色切替できる必要がある。
            if (instrument is GbPulseInstrument && !document.ContainsKey("dutyMacro"))
            {
                document["dutyMacro"] = null;
            }
            if (instrument is SnesSampleInstrument && !document.ContainsKey("volumeMacro"))
            {
                document["volumeMacro"] = null;
            }
            return document;
        }

        private static void RequireProperty(JsonObject document, string propertyName, string optionName)
        {
            if (!document.ContainsKey(propertyName))
            {
                throw new ArgumentException($"音色 {document["kind"]} には {optionName} を指定できません。");
            }
        }

        private static JsonNode? ParseMacro(string text)
        {
            if (string.Equals(text.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            Macro macro = MacroOption.Parse(text);
            return new JsonObject { ["values"] = JsonSerializer.SerializeToNode(macro.Values), ["loopIndex"] = macro.LoopIndex };
        }

        private static JsonNode ParseDuty(string text)
        {
            DutyCycle duty = text.Trim() switch
            {
                "12.5" => DutyCycle.Percent12_5,
                "25" => DutyCycle.Percent25,
                "50" => DutyCycle.Percent50,
                "75" => DutyCycle.Percent75,
                _ => throw new ArgumentException("duty は 12.5 / 25 / 50 / 75 です。")
            };
            return JsonValue.Create(duty.ToString())!;
        }

        private static JsonNode ParseEnum<TEnum>(string text) where TEnum : struct, Enum
        {
            if (!Enum.TryParse(text, true, out TEnum value) || !Enum.IsDefined(value) || Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0)
            {
                throw new ArgumentException($"{typeof(TEnum).Name} の値が不正です: {text}");
            }
            return JsonValue.Create(value.ToString())!;
        }

        private static JsonNode ParseInteger(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new ArgumentException($"整数を指定してください: {text}");
            }
            return JsonValue.Create(value)!;
        }

        private static JsonNode ParseBoolean(string text)
        {
            if (!bool.TryParse(text, out bool value))
            {
                throw new ArgumentException($"true または false を指定してください: {text}");
            }
            return JsonValue.Create(value)!;
        }

        private static JsonNode ParseDouble(string text)
        {
            return JsonValue.Create(ReadDouble(text))!;
        }

        private static double ReadDouble(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                throw new ArgumentException($"有限の数値を指定してください: {text}");
            }
            return value;
        }

        private static JsonNode ParseRegisters(string text)
        {
            int[] values = MacroOption.ParseValues(text);
            if (values.Length != EnvelopeValueCount)
            {
                throw new ArgumentException("ADSR レジスタは attack,decay,sustainLevel,sustainRate の 4 値です。");
            }
            return new JsonObject
            {
                ["attack"] = values[0], ["decay"] = values[1],
                ["sustainLevel"] = values[2], ["sustainRate"] = values[3]
            };
        }

        private static JsonNode ParseEnvelope(string text)
        {
            string[] values = text.Split(',');
            if (values.Length != EnvelopeValueCount)
            {
                throw new ArgumentException("ADSR は attack秒,decay秒,sustain(0〜1),release秒 の 4 値です。");
            }
            return new JsonObject
            {
                ["attackSeconds"] = ReadDouble(values[0]),
                ["decaySeconds"] = ReadDouble(values[1]),
                ["sustainLevel"] = ReadDouble(values[2]),
                ["releaseSeconds"] = ReadDouble(values[3])
            };
        }
    }
}
