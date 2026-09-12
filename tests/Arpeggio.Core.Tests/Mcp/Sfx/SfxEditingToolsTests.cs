using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Sfx
{
    /// <summary>全編集操作の予行・保存・履歴と、CLI/Core 間の同一結果を検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxEditingToolsTests
    {
        private const string Patch = "{\"tone\":{\"baseFrequencyHz\":660}}";
        private const string Locks = "[\"tone.baseFrequencyHz\",\"tone.baseFrequencyHz\"]";
        private const double MutationStrength = 0.5;

        /// <summary>全五操作と三チップの保存・予行境界。</summary>
        public static IEnumerable<object[]> Operations()
        {
            foreach (string chip in new[] { "nes", "gameboy", "snes" })
            {
                foreach (string operation in new[] { "tweak", "randomize", "mutate", "regenerate", "detach" })
                {
                    yield return new object[] { chip, operation };
                }
            }
        }

        /// <summary>予行は非破壊、実適用は一履歴となり、全JSON・保存結果がCLIと一致する。</summary>
        [Theory]
        [MemberData(nameof(Operations))]
        public void DryRunAndApplyMatchCliWithOneUndo(string chip, string operation)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen(chip);
            if (operation == "regenerate")
            {
                fixture.Session.Song!.Tracks[0].Notes[0].Volume = 3;
                SfxToolFixture.Success(fixture.Tools.SaveSong());
            }
            string commandPath = fixture.PathFor("command.json");
            File.Copy(path, commandPath);
            string patchPath = fixture.PathFor("patch.json");
            File.WriteAllText(patchPath, Patch);
            byte[] originalBytes = File.ReadAllBytes(path);
            Song original = fixture.Session.Song!;
            DateTime originalTime = File.GetLastWriteTimeUtc(path);
            string revision = SfxHash.ComputeRevision(original);
            string[] arguments = CommandArguments(commandPath, patchPath, operation, revision);
            string previewJson = Invoke(fixture.Tools, operation, revision, dryRun: true);
            SfxParameterToolsTests.AssertCliEquals(previewJson, arguments.Concat(new[] { "--dry-run" }).ToArray());
            JsonElement preview = SfxToolFixture.Success(previewJson);
            Assert.True(preview.GetProperty("changed").GetBoolean());
            Assert.True(preview.GetProperty("dryRun").GetBoolean());
            Assert.Equal(revision, preview.GetProperty("revision").GetString());
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Equal(originalTime, File.GetLastWriteTimeUtc(path));
            Assert.Equal(0, fixture.Session.History.UndoCount);
            string appliedJson = Invoke(fixture.Tools, operation, revision);
            SfxParameterToolsTests.AssertCliEquals(appliedJson, arguments);
            JsonElement applied = SfxToolFixture.Success(appliedJson);
            Assert.Equal(preview.GetProperty("candidateRevision").GetString(), applied.GetProperty("revision").GetString());
            Assert.Equal(SfxHash.ComputeRevision(original), applied.GetProperty("revision").GetString());
            Assert.Same(original, fixture.Session.Song);
            Assert.Equal(File.ReadAllBytes(commandPath), File.ReadAllBytes(path));
            Assert.Equal(1, fixture.Session.History.UndoCount);
            SfxToolFixture.Success(fixture.Tools.Undo());
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            SfxToolFixture.Success(fixture.Tools.Redo());
            Assert.Equal(File.ReadAllBytes(commandPath), File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
        }

        /// <summary>uint32端点と any の選択・locks が、同じ Core の正規化値と出自になる。</summary>
        [Theory]
        [InlineData("nes", 0U)]
        [InlineData("gameboy", 1U)]
        [InlineData("snes", uint.MaxValue)]
        public void RandomizationMatchesCoreAndPreservesProvenance(string chip, uint seed)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen(chip);
            Song song = fixture.Session.Song!;
            SfxParameterRandomizationResult randomized = SfxParameterRandomizer.Randomize(
                song.Sfx!.Known!.Parameters, song.Chip, "any", seed);
            JsonElement result = SfxToolFixture.Success(fixture.Tools.RandomizeSfx("any", seed));
            Assert.Equal(randomized.Parameters, song.Sfx.Known!.Parameters);
            Assert.Equal(randomized.SourcePreset, song.Sfx.Known.SourcePreset);
            Assert.Equal("any", result.GetProperty("randomization").GetProperty("category").GetString());
            Assert.Equal(seed, result.GetProperty("randomization").GetProperty("seed").GetUInt32());
            SfxParameters before = song.Sfx.Known.Parameters;
            string[] locks = { "tone.baseFrequencyHz", "tone.baseFrequencyHz" };
            SfxParameterRandomizationResult mutated = SfxParameterRandomizer.Mutate(before, song.Chip, seed, MutationStrength, locks);
            SfxToolFixture.Success(fixture.Tools.MutateSfx(seed, MutationStrength, Locks));
            Assert.Equal(mutated.Parameters, song.Sfx.Known.Parameters);
            Assert.Equal(before.Tone.BaseFrequencyHz, song.Sfx.Known.Parameters.Tone.BaseFrequencyHz);
            SfxRandomization provenance = song.Sfx.Known.LastRandomization!;
            Assert.Equal(SfxHash.ComputeParametersHash(before, song.Chip), provenance.BaseParametersHash);
            Assert.Equal("tone.baseFrequencyHz", Assert.Single(provenance.Locks!));
            string randomizedJson = SongSerializer.Serialize(SfxEditor.CreateCandidate(randomized.Parameters, song.Chip,
                song.Title, randomized.SourcePreset, randomized.Randomization).Song);
            SfxToolFixture.Success(fixture.Tools.Undo());
            Assert.Equal(randomizedJson, File.ReadAllText(path));
            SfxToolFixture.Success(fixture.Tools.Redo());
            SfxToolFixture.Success(fixture.Tools.TweakSfx("{\"tone\":{\"baseFrequencyHz\":777}}"));
            Assert.Equal(SessionOutput.Serialize(provenance), SessionOutput.Serialize(song.Sfx.Known.LastRandomization!));
        }

        /// <summary>同値patch・strength0・全ロック・同じ乱数結果では保存日時と redo を維持する。</summary>
        [Fact]
        public void NoOpPreservesFileHistoryAndRandomization()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen();
            SfxToolFixture.Success(fixture.Tools.RandomizeSfx("hit", 1));
            double frequency = fixture.Session.Song!.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz;
            SfxToolFixture.Success(fixture.Tools.TweakSfx(Patch));
            SfxToolFixture.Success(fixture.Tools.Undo());
            byte[] original = File.ReadAllBytes(path);
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            string allLocks = JsonSerializer.Serialize(SfxParameterCatalog.GetAll()
                .Where(description => description.IsSupported(ChipKind.Nes)).Select(description => description.Path));
            string equivalentPatch = "{\"tone\":{\"baseFrequencyHz\":" + frequency.ToString(CultureInfo.InvariantCulture) + "}}";
            string[] replies =
            {
                fixture.Tools.TweakSfx(equivalentPatch), fixture.Tools.MutateSfx(1, strength: 0),
                fixture.Tools.MutateSfx(1, locks: allLocks), fixture.Tools.RandomizeSfx("hit", 1),
                fixture.Tools.RegenerateSfx(true)
            };
            Assert.All(replies, reply => Assert.False(SfxToolFixture.Success(reply).GetProperty("changed").GetBoolean()));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
            Assert.Equal(1, fixture.Session.History.UndoCount);
            Assert.Equal(1, fixture.Session.History.RedoCount);
        }

        internal static string Invoke(ArpeggioTools tools, string operation, string? revision = null, bool dryRun = false)
        {
            return operation switch
            {
                "tweak" => tools.TweakSfx(Patch, revision, dryRun),
                "randomize" => tools.RandomizeSfx("hit", uint.MaxValue, revision, dryRun),
                "mutate" => tools.MutateSfx(uint.MaxValue, MutationStrength, Locks, revision, dryRun),
                "regenerate" => tools.RegenerateSfx(true, revision, dryRun),
                "detach" => tools.DetachSfx(revision, dryRun),
                _ => throw new ArgumentException("未知の操作です。", nameof(operation))
            };
        }

        private static string[] CommandArguments(string path, string patchPath, string operation, string revision)
        {
            string seed = uint.MaxValue.ToString(CultureInfo.InvariantCulture);
            string[] options = operation switch
            {
                "tweak" => new[] { "--patch", patchPath },
                "randomize" => new[] { "--category", "hit", "--seed", seed },
                "mutate" => new[] { "--seed", seed, "--strength", "0.5", "--lock", "tone.baseFrequencyHz", "--lock", "tone.baseFrequencyHz" },
                "regenerate" => new[] { "--replace-generated" },
                _ => Array.Empty<string>()
            };
            return new[] { "sfx", operation, path, "--expected-revision", revision }.Concat(options).ToArray();
        }
    }
}
