using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>現在音と保存意図を区別して、SFX の共通結果と生成診断を表示する。</summary>
    internal static class SfxOutput
    {
        private static readonly string[] Limitations =
        {
            "制御は60 Hz、音量は0〜15の16段階（非ゼロ15段階）、音程はチップ固有の周期へ量子化されます。",
            "punch はピークを保ち、保持冒頭以外の相対音量を下げます。",
            "チップ間の音量一致と、旧アプリによる新マクロの同音再生は保証しません。"
        };

        internal static Dictionary<string, object?> Describe(string operation, Song song,
            SfxSongCompilationResult? generation, bool changed = false, bool dryRun = false, string? revision = null)
        {
            SfxSynchronizationState synchronization = SfxSynchronization.Inspect(song);
            return new Dictionary<string, object?>
            {
                ["operation"] = operation, ["changed"] = changed, ["dryRun"] = dryRun,
                ["editable"] = synchronization.Editable, ["reason"] = synchronization.Reason.ToString(),
                ["reasons"] = synchronization.Reasons.Select(reason => reason.ToString()).ToArray(),
                ["revision"] = revision, ["candidateRevision"] = SfxHash.ComputeRevision(song),
                ["parameters"] = Parameters(synchronization.Parameters),
                ["savedParameters"] = Parameters(synchronization.SavedParameters),
                ["generation"] = Generation(generation, song),
                ["randomization"] = Randomization(song.Sfx?.Known?.LastRandomization),
                ["sourcePreset"] = song.Sfx?.Known?.SourcePreset,
                ["warnings"] = generation?.Warnings ?? Array.Empty<SfxGenerationWarning>(),
                ["limitations"] = Limitations
            };
        }

        internal static object Edit(SfxEditResult result)
        {
            Dictionary<string, object?> output = Describe(result.Operation, result.Candidate, result.Generation,
                result.Changed, result.DryRun, result.Revision);
            output["replacement"] = result.Replacement;
            output["changes"] = result.Changes;
            return output;
        }

        internal static object Schema(ChipKind chip)
        {
            return SfxParameterCatalog.GetAll().Select(description => new
            {
                description.Path, description.Chip, description.ValueKind, description.Unit,
                description.DefaultValue, description.Minimum, description.Maximum, description.AllowsZero,
                description.Choices, description.Step, description.StepUnit, description.FineStep,
                description.IsLogarithmic, description.Description, supported = description.IsSupported(chip)
            }).ToArray();
        }

        internal static JsonNode? Parameters(SfxParameters? parameters)
        {
            if (parameters == null)
            {
                return null;
            }
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return JsonSerializer.SerializeToNode(parameters, options);
        }

        internal static void WriteText(object output)
        {
            using JsonDocument document = JsonDocument.Parse(SessionOutput.Serialize(output));
            JsonElement root = document.RootElement;
            Console.WriteLine($"{root.GetProperty("operation").GetString()}: 完了");
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name == "warnings")
                {
                    foreach (JsonElement warning in property.Value.EnumerateArray())
                    {
                        Console.Error.WriteLine($"{warning.GetProperty("code").GetString()}: {warning.GetProperty("message").GetString()}");
                    }
                    continue;
                }
                if (property.Name != "operation")
                {
                    Console.WriteLine($"{property.Name}: {property.Value}");
                }
            }
        }

        private static object? Randomization(SfxRandomization? randomization)
        {
            if (randomization == null)
            {
                return null;
            }
            return new
            {
                operation = randomization.Operation.ToString().ToLowerInvariant(),
                randomization.AlgorithmVersion, randomization.Seed, randomization.Category,
                randomization.Strength, randomization.Locks, randomization.BaseParametersHash
            };
        }

        private static object? Generation(SfxSongCompilationResult? generation, Song song)
        {
            if (generation == null)
            {
                return null;
            }
            return new
            {
                generation.Curves.GeneratorVersion, song.TempoBpm, song.LengthTicks,
                generation.Curves.BodyDurationSeconds,
                tone = Layer(generation.Curves.Tone?.Envelope, generation.ToneTrackIndex),
                noise = Layer(generation.Curves.Noise, generation.NoiseTrackIndex),
                generatedHash = song.Sfx?.Known?.GeneratedHash
            };
        }

        private static object? Layer(SfxEnvelopeCurve? envelope, int? trackIndex)
        {
            return envelope == null ? null : new
            {
                envelope.RequestedEnvelopeSeconds, envelope.EnvelopeFrames, trackIndex
            };
        }
    }
}
