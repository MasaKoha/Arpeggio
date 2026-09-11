using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Sfx
{
    /// <summary>カテゴリ生成・変異の出自、決定性、ロックと入力契約を検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxExplorationCommandsTests
    {
        /// <summary>固定 seed の候補と適用結果が一致し、チップ・title と出自が保存・履歴を往復する。</summary>
        [Theory]
        [InlineData("nes", "0")]
        [InlineData("gameboy", "1")]
        [InlineData("snes", "4294967295")]
        public void RandomizeAndMutatePreserveProvenanceAndUndo(string chip, string seed)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path, "--chip", chip, "--title", "保持するタイトル");
            string original = File.ReadAllText(path);
            JsonElement preview = SfxParameterCommandsTests.AssertSuccess("sfx", "randomize", path,
                "--category", "any", "--seed", seed, "--dry-run");
            Assert.Equal(original, File.ReadAllText(path));
            JsonElement applied = SfxParameterCommandsTests.AssertSuccess("sfx", "randomize", path,
                "--category", "any", "--seed", seed, "--expected-revision", preview.GetProperty("revision").GetString()!);
            Assert.Equal(preview.GetProperty("candidateRevision").GetString(), applied.GetProperty("revision").GetString());
            Assert.Equal("randomize", applied.GetProperty("randomization").GetProperty("operation").GetString());
            Song randomized = SongSerializer.Load(path);
            Assert.Equal("保持するタイトル", randomized.Title);
            Assert.Equal(ChipReference.ParseChip(chip), randomized.Chip);
            SfxDefinitionData definition = randomized.Sfx!.Known!;
            Assert.Equal("any", definition.LastRandomization!.Category);
            Assert.Equal(uint.Parse(seed, CultureInfo.InvariantCulture), definition.LastRandomization.Seed);
            Assert.NotNull(definition.SourcePreset);
            string randomBytes = File.ReadAllText(path);
            string[] locks = { "tone.baseFrequencyHz", "noise.envelope.volume" };
            SfxParameterCommandsTests.AssertSuccess("sfx", "mutate", path, "--seed", seed, "--strength", "1",
                "--lock", locks[0], "--lock", locks[1]);
            Song mutated = SongSerializer.Load(path);
            SfxDefinitionData mutation = mutated.Sfx!.Known!;
            Assert.Equal(definition.Parameters.Tone.BaseFrequencyHz, mutation.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(definition.Parameters.Noise.Envelope.Volume, mutation.Parameters.Noise.Envelope.Volume);
            Assert.Equal(locks, mutation.LastRandomization!.Locks);
            Assert.Equal(SfxHash.ComputeParametersHash(definition.Parameters, randomized.Chip), mutation.LastRandomization.BaseParametersHash);
            Assert.Equal(definition.SourcePreset, mutation.SourcePreset);
            string mutatedBytes = File.ReadAllText(path);
            AssertHistory(path, 2, 0);
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            Assert.Equal(randomBytes, File.ReadAllText(path));
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Equal(0, CliConversionFixture.Invoke("redo", path).ExitCode);
            Assert.Equal(0, CliConversionFixture.Invoke("redo", path).ExitCode);
            Assert.Equal(mutatedBytes, File.ReadAllText(path));
            SfxParameterCommandsTests.AssertSuccess("sfx", "tweak", path, "--vibrato-speed", "12");
            Assert.Equal(SessionOutput.Serialize(mutation.LastRandomization!),
                SessionOutput.Serialize(SongSerializer.Load(path).Sfx!.Known!.LastRandomization!));
        }

        /// <summary>再実行 randomize・strength0・全項目ロックは出自・履歴・ファイル日時を保持する。</summary>
        [Fact]
        public void EquivalentExplorationDoesNotSaveOrClearRedo()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            SfxParameterCommandsTests.AssertSuccess("sfx", "randomize", path, "--category", "laser", "--seed", "1");
            SfxParameterCommandsTests.AssertSuccess("sfx", "tweak", path, "--frequency", "330");
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            byte[] before = File.ReadAllBytes(path);
            byte[] history = File.ReadAllBytes(path + ".history/state.json");
            DateTime timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, timestamp);
            Assert.False(SfxParameterCommandsTests.AssertSuccess("sfx", "randomize", path,
                "--category", "laser", "--seed", "1").GetProperty("changed").GetBoolean());
            Assert.False(SfxParameterCommandsTests.AssertSuccess("sfx", "mutate", path,
                "--seed", "2", "--strength", "0").GetProperty("changed").GetBoolean());
            string[] locked = SfxParameterCatalog.GetAll(ChipKind.Nes)
                .SelectMany(description => new[] { "--lock", description.Path }).ToArray();
            Assert.False(SfxParameterCommandsTests.AssertSuccess(new[] { "sfx", "mutate", path, "--seed", "3" }
                .Concat(locked).ToArray()).GetProperty("changed").GetBoolean());
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(history, File.ReadAllBytes(path + ".history/state.json"));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
            AssertHistory(path, 1, 1);
        }

        /// <summary>必須引数・seed端点・強度・ロック・置換明示を単一JSONで拒否する。</summary>
        [Theory]
        [InlineData("randomize --category any")]
        [InlineData("randomize --seed 1")]
        [InlineData("randomize --category any --seed 4294967296")]
        [InlineData("randomize --category any --seed -1")]
        [InlineData("mutate --seed 1 --strength NaN")]
        [InlineData("mutate --seed 1 --strength 1,0")]
        [InlineData("mutate --seed 1 --strength 1.1")]
        [InlineData("mutate --seed 1 --lock")]
        [InlineData("regenerate")]
        [InlineData("regenerate --replace-generated=false")]
        public void ExplorationArgumentsAreValidated(string command)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            string[] tokens = command.Split(' ');
            SfxCommandFailureTests.AssertFailure(1, "InvalidParameter", new[] { "sfx", tokens[0], path }
                .Concat(tokens.Skip(1)).ToArray());
            SfxCommandFailureTests.AssertFailure(1, "UnsupportedParameter", "sfx", "mutate", path,
                "--seed", "1", "--lock", "tone.unknown");
        }

        internal static void AssertHistory(string path, int undoCount, int redoCount)
        {
            using JsonDocument history = JsonDocument.Parse(File.ReadAllText(path + ".history/state.json"));
            Assert.Equal(undoCount, history.RootElement.GetProperty("undo").GetArrayLength());
            Assert.Equal(redoCount, history.RootElement.GetProperty("redo").GetArrayLength());
        }
    }
}
