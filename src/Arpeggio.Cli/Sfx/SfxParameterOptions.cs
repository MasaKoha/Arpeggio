using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>個別 CLI 名を正規パスへ写像し、Core と同じ部分 patch に変換する。</summary>
    internal sealed class SfxParameterOptions
    {
        private readonly Dictionary<string, Option<string>> _options = new Dictionary<string, Option<string>>();
        private readonly Option<string?> _patch = new Option<string?>("--patch")
        {
            Description = "parameters の部分 JSON ファイル。- は標準入力。個別指定と併用不可。"
        };

        internal SfxParameterOptions(Command command)
        {
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll())
            {
                string name = Name(description.Path);
                if (_options.TryGetValue(name, out Option<string>? existing))
                {
                    existing.Description += $" / {description.Path} [{description.Unit}] {description.Description}";
                    continue;
                }
                var option = new Option<string>(name)
                {
                    Arity = ArgumentArity.ExactlyOne,
                    Description = $"{description.Path} [{description.Unit}] {description.Description}"
                };
                option.Validators.Add(CliArgumentGuard.RejectFlagLikeToken);
                _options.Add(name, option);
                command.Options.Add(option);
            }
            command.Options.Add(_patch);
        }

        internal string Read(ParseResult result, ChipKind chip)
        {
            var supplied = _options.Where(pair => result.GetResult(pair.Value) != null).ToArray();
            RequireSingleUsage(result, _patch, "patch");
            string? patch = result.GetValue(_patch);
            if (patch != null)
            {
                if (supplied.Length != 0)
                {
                    throw new SfxParameterException("InvalidParameter", "patch", "patch と個別指定は併用できません。");
                }
                return patch == "-" ? Console.In.ReadToEnd() : File.ReadAllText(patch);
            }
            var root = new JsonObject();
            foreach (var pair in supplied)
            {
                SfxParameterDescription? description = SfxParameterCatalog.GetAll(chip)
                    .FirstOrDefault(parameter => Name(parameter.Path) == pair.Key);
                if (description == null)
                {
                    string path = SfxParameterCatalog.GetAll().First(parameter => Name(parameter.Path) == pair.Key).Path;
                    throw new SfxParameterException("UnsupportedParameter", path, $"{pair.Key} は現在のチップでは使用できません。");
                }
                RequireSingleUsage(result, pair.Value, description.Path);
                Add(root, description, result.GetValue(pair.Value)!);
            }
            return root.ToJsonString();
        }

        private static void RequireSingleUsage(ParseResult result, Option option, string path)
        {
            if (result.GetResult(option) is { IdentifierTokenCount: > 1 })
            {
                throw new SfxParameterException("InvalidParameter", path, "同じ項目を重複して指定できません。");
            }
        }

        private static void Add(JsonObject root, SfxParameterDescription description, string value)
        {
            string[] segments = description.Path.Split('.');
            JsonObject parent = root;
            foreach (string segment in segments.Take(segments.Length - 1))
            {
                if (parent[segment] == null)
                {
                    parent[segment] = new JsonObject();
                }
                parent = (JsonObject)parent[segment]!;
            }
            try
            {
                // 数値を double へ変換しないことで、整数精度や微小値の拒否も patch と一致させる。
                parent[segments[^1]] = description.ValueKind == SfxParameterValueKind.Choice
                    ? JsonValue.Create(value) : JsonNode.Parse(value);
            }
            catch (JsonException exception)
            {
                throw new SfxParameterException("InvalidParameter", description.Path, "値の形式が不正です。", exception);
            }
        }

        private static string Name(string path)
        {
            return path switch
            {
                "tone.enabled" => "--tone-enabled", "noise.enabled" => "--noise-enabled",
                "tone.baseFrequencyHz" => "--frequency", "tone.slideSemitonesPerSecond" => "--slide",
                "tone.deltaSlideSemitonesPerSecondSquared" => "--delta-slide",
                "tone.vibratoDepthCents" => "--vibrato-depth", "tone.vibratoSpeedHz" => "--vibrato-speed",
                "tone.pitchChangeSemitones" => "--pitch-change", "tone.pitchChangeTimeSeconds" => "--pitch-change-time",
                "tone.repeatPeriodSeconds" => "--repeat-period",
                "tone.envelope.volume" => "--volume", "tone.envelope.attackSeconds" => "--attack",
                "tone.envelope.sustainSeconds" => "--sustain", "tone.envelope.decaySeconds" => "--decay",
                "tone.envelope.punch" => "--punch", "noise.envelope.volume" => "--noise-volume",
                "noise.envelope.attackSeconds" => "--noise-attack", "noise.envelope.sustainSeconds" => "--noise-sustain",
                "noise.envelope.decaySeconds" => "--noise-decay", "noise.envelope.punch" => "--noise-punch",
                "nes.dutyPercent" or "gameBoy.dutyPercent" => "--duty",
                "nes.dutySweepPercentPerSecond" or "gameBoy.dutySweepPercentPerSecond" => "--duty-sweep",
                "nes.noisePeriodIndex" => "--noise-period", "nes.noiseMode" => "--noise-mode",
                "gameBoy.noiseSelection" => "--noise-selection", "gameBoy.noiseWidth" => "--noise-width",
                "nes.noiseSlideIndicesPerSecond" or "gameBoy.noiseSlideSelectionsPerSecond" => "--noise-slide",
                "snes.waveform" => "--waveform", "snes.noiseRate" => "--noise-rate",
                _ => throw new InvalidOperationException($"CLI 名が未定義です: {path}")
            };
        }
    }
}
