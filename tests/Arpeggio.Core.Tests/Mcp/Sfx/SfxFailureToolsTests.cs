using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;
using Arpeggio.Core.Tests.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Sfx
{
    /// <summary>入力・同期状態・revision・保存失敗を JSON へ分類し、部分適用を防ぐ。</summary>
    public sealed class SfxFailureToolsTests
    {
        /// <summary>部分patchの途中に不正値があっても、先に読んだ変更を保存しない。</summary>
        [Theory]
        [InlineData("{", "InvalidParameter", "")]
        [InlineData("{}", "InvalidParameter", "")]
        [InlineData("[]", "InvalidParameter", "")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":660,\"enabled\":null}}", "InvalidParameter", "tone.enabled")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":660,\"baseFrequencyHz\":880}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":660,\"envelope\":{\"volume\":16}}}", "InvalidParameter", "tone.envelope.volume")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":660,\"unknown\":1}}", "UnsupportedParameter", "tone.unknown")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":660},\"snes\":{\"noiseRate\":20}}", "UnsupportedParameter", "snes")]
        public void InvalidPatchLeavesNoPartialChanges(string patch, string code, string parameterPath)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen();
            byte[] before = File.ReadAllBytes(path);
            SfxToolFixture.Failure(fixture.Tools.TweakSfx(patch), 1, code, parameterPath);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(0, fixture.Session.History.UndoCount);
            Assert.Equal(0, fixture.Session.History.RedoCount);
        }

        /// <summary>locks はJSON文字列配列に限定し、未知パスの検証は Core へ委譲する。</summary>
        [Theory]
        [InlineData("{", "InvalidParameter", "locks")]
        [InlineData("{}", "InvalidParameter", "locks")]
        [InlineData("null", "InvalidParameter", "locks")]
        [InlineData("[null]", "InvalidParameter", "locks")]
        [InlineData("[1]", "InvalidParameter", "locks")]
        [InlineData("[\"tone.baseFrequencyHz\",false]", "InvalidParameter", "locks")]
        [InlineData("[\"unknown\"]", "UnsupportedParameter", "unknown")]
        [InlineData("[\"snes.noiseRate\"]", "UnsupportedParameter", "snes.noiseRate")]
        public void InvalidLocksReturnStableErrors(string locks, string code, string parameterPath)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen();
            byte[] before = File.ReadAllBytes(path);
            SfxToolFixture.Failure(fixture.Tools.MutateSfx(1, locks: locks), 1, code, parameterPath);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(0, fixture.Session.History.UndoCount);
        }

        /// <summary>文書不正と入力不正を分け、未オープンでも例外を外へ漏らさない。</summary>
        [Fact]
        public void MissingSongInvalidInputAndInvalidDocumentUseDistinctErrors()
        {
            using var fixture = new SfxToolFixture();
            foreach (string operation in new[] { "tweak", "randomize", "mutate", "regenerate", "detach" })
            {
                SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation), 1, "InvalidParameter");
            }
            string path = fixture.CreateAndOpen();
            byte[] before = File.ReadAllBytes(path);
            SfxToolFixture.Failure(fixture.Tools.RegenerateSfx(false), 1, "InvalidParameter", "replaceGenerated");
            SfxToolFixture.Failure(fixture.Tools.RandomizeSfx("unknown", 1), 1, "InvalidParameter", "preset");
            foreach (double strength in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
            {
                SfxToolFixture.Failure(fixture.Tools.MutateSfx(1, strength), 1, "InvalidParameter", "strength");
            }
            fixture.Session.Song!.TempoBpm = 0;
            SfxToolFixture.Failure(fixture.Tools.SfxParameters(), 2, "InvalidSong", "document");
            SfxToolFixture.Failure(fixture.Tools.TweakSfx("{}"), 2, "InvalidSong", "document");
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(0, fixture.Session.History.UndoCount);
        }

        /// <summary>全編集操作が stale revision・外部更新・保存不能を拒否し、公開Songと履歴を保つ。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("randomize")]
        [InlineData("mutate")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public void RevisionAndIoFailureDoNotPublishCandidate(string operation)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen();
            if (operation == "regenerate")
            {
                fixture.Session.Song!.Tracks[0].Notes[0].Volume = 3;
                SfxToolFixture.Success(fixture.Tools.SaveSong());
            }
            Song original = fixture.Session.Song!;
            string originalJson = SongSerializer.Serialize(original);
            SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation, "stale"), 1, "RevisionConflict", "expectedRevision");
            SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation, "stale", dryRun: true), 1, "RevisionConflict", "expectedRevision");
            Song external = SongSerializer.Load(path);
            external.Title = "外部変更";
            SongSerializer.Save(external, path);
            byte[] externalBytes = File.ReadAllBytes(path);
            SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation), 1, "RevisionConflict", "expectedRevision");
            Assert.Equal(externalBytes, File.ReadAllBytes(path));
            File.Delete(path);
            Directory.CreateDirectory(path);
            SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation), 3, "InputOutputError", "path");
            Assert.Same(original, fixture.Session.Song);
            Assert.Equal(originalJson, SongSerializer.Serialize(original));
            Assert.Equal(0, fixture.Session.History.UndoCount);
            Assert.Equal(0, fixture.Session.History.RedoCount);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>保存値と生成列を別々に診断し、明示再生成だけが両hashを修復する。</summary>
        [Fact]
        public void RegenerateRepairsBothHashesAndUndoRestoresManualContent()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen();
            Song song = fixture.Session.Song!;
            song.Tracks[0].Notes[0].Volume = 3;
            JsonElement generatedChanged = SfxToolFixture.Success(fixture.Tools.SfxParameters());
            Assert.Equal("GeneratedContentChanged", generatedChanged.GetProperty("reason").GetString());
            SfxToolFixture.Failure(fixture.Tools.MutateSfx(1), 1, "GeneratedContentChanged");
            SfxDefinitionData definition = song.Sfx!.Known!;
            song.Sfx = new SfxDefinition(definition with
            {
                Parameters = definition.Parameters with { Tone = definition.Parameters.Tone with { BaseFrequencyHz = 660 } }
            });
            SfxToolFixture.Success(fixture.Tools.SaveSong());
            byte[] manual = File.ReadAllBytes(path);
            JsonElement result = SfxToolFixture.Success(fixture.Tools.SfxParameters());
            Assert.Equal("SavedParametersChanged", result.GetProperty("reason").GetString());
            Assert.Equal(2, result.GetProperty("reasons").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("parameters").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("generation").ValueKind);
            Assert.Equal(660, result.GetProperty("savedParameters").GetProperty("tone").GetProperty("baseFrequencyHz").GetDouble());
            SfxToolFixture.Failure(fixture.Tools.RandomizeSfx("hit", 1), 1, "SavedParametersChanged");
            SfxToolFixture.Success(fixture.Tools.RegenerateSfx(true));
            Assert.True(SfxSynchronization.Inspect(song).Editable);
            SfxToolFixture.Success(fixture.Tools.Undo());
            Assert.Equal(manual, File.ReadAllBytes(path));
        }

        /// <summary>未知三版は再編集できず、detach と Undo は不透明JSONと生成列を保持する。</summary>
        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("generatorVersion")]
        [InlineData("algorithmVersion")]
        public void UnknownVersionOnlyAllowsDetach(string versionProperty)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.PathFor("unknown.json");
            SongSerializer.Save(SfxEditFixture.CreateUnsupportedSong(versionProperty), path);
            SfxToolFixture.Success(fixture.Tools.OpenSong(path));
            byte[] before = File.ReadAllBytes(path);
            string generatedHash = SfxHash.ComputeGeneratedHash(fixture.Session.Song!);
            Assert.Equal("UnsupportedSfxVersion", SfxToolFixture.Success(fixture.Tools.SfxParameters()).GetProperty("reason").GetString());
            foreach (string operation in new[] { "tweak", "randomize", "mutate", "regenerate" })
            {
                SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation), 1, "UnsupportedSfxVersion", "sfx");
            }
            SfxToolFixture.Success(fixture.Tools.DetachSfx());
            Assert.Null(fixture.Session.Song!.Sfx);
            Assert.Equal(generatedHash, SfxHash.ComputeGeneratedHash(fixture.Session.Song));
            SfxToolFixture.Success(fixture.Tools.Undo());
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        /// <summary>従来プリセットと通常ソングに定義を暗黙追加しない。</summary>
        [Fact]
        public void LegacySongRejectsParameterEditing()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.PathFor("legacy.json");
            SfxToolFixture.Success(fixture.Tools.NewSfx(path, "jump"));
            Assert.Equal(SongSerializer.Serialize(SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.Jump)), File.ReadAllText(path));
            Assert.Equal("MissingDefinition", SfxToolFixture.Success(fixture.Tools.SfxParameters()).GetProperty("reason").GetString());
            foreach (string operation in new[] { "tweak", "randomize", "mutate", "regenerate", "detach" })
            {
                SfxToolFixture.Failure(SfxEditingToolsTests.Invoke(fixture.Tools, operation), 1, "MissingDefinition", "sfx");
            }
            Assert.Null(fixture.Session.Song!.Sfx);
            Assert.Equal(0, fixture.Session.History.UndoCount);
        }
    }
}
