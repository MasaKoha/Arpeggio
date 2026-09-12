using System.IO;
using System.Text;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Sfx
{
    /// <summary>新規保存の Core 一致と、作成・予行・失敗時の現セッション維持を検証する。</summary>
    public sealed class SfxCreationToolsTests
    {
        /// <summary>八用途×三チップの保存内容は Core と全バイト一致し、作成だけでは開かない。</summary>
        [Theory]
        [MemberData(nameof(SfxParameterPresetCatalogTests.Cases), MemberType = typeof(SfxParameterPresetCatalogTests))]
        public void CreateMatchesCoreWithoutOpening(ChipKind chip, SfxPresetKind kind)
        {
            using var fixture = new SfxToolFixture();
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            Song expected = SfxEditor.CreateCandidate(preset.Parameters, chip, preset.Name, preset.Name).Song;
            string path = fixture.PathFor("created.json");
            JsonElement preview = SfxToolFixture.Success(fixture.Tools.CreateSfx(path, chip.ToString(), preset.Name, dryRun: true));
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("revision").ValueKind);
            Assert.False(File.Exists(path));
            JsonElement created = SfxToolFixture.Success(fixture.Tools.CreateSfx(path, chip.ToString(), preset.Name));
            Assert.Equal(new UTF8Encoding(false).GetBytes(SongSerializer.Serialize(expected)), File.ReadAllBytes(path));
            Assert.Equal(preview.GetProperty("candidateRevision").GetString(), created.GetProperty("revision").GetString());
            Assert.Equal(SfxHash.ComputeRevision(expected), created.GetProperty("revision").GetString());
            Assert.Equal(preview.GetProperty("generation").GetRawText(), created.GetProperty("generation").GetRawText());
            Assert.Null(fixture.Session.Song);
            Assert.Null(fixture.Session.Path);
            Assert.Equal(0, fixture.Session.History.UndoCount);
            Assert.Equal(0, fixture.Session.History.RedoCount);
            Assert.False(Directory.Exists(path + ".history"));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>成功・予行・既存先・不正引数・I/O失敗でも、現曲と両履歴・古い側車は変わらない。</summary>
        [Fact]
        public void EveryCreationOutcomePreservesCurrentSessionAndHistory()
        {
            using var fixture = new SfxToolFixture();
            string currentPath = fixture.CreateAndOpen();
            SfxToolFixture.Success(fixture.Tools.TweakSfx("{\"tone\":{\"baseFrequencyHz\":330}}"));
            SfxToolFixture.Success(fixture.Tools.TweakSfx("{\"tone\":{\"baseFrequencyHz\":550}}"));
            SfxToolFixture.Success(fixture.Tools.Undo());
            Song original = fixture.Session.Song!;
            byte[] bytes = File.ReadAllBytes(currentPath);
            string information = fixture.Tools.SongInfo();
            string destination = fixture.PathFor("candidate.json");
            Directory.CreateDirectory(destination + ".history");
            string sidecar = Path.Combine(destination + ".history", "state.json");
            File.WriteAllText(sidecar, "既存の側車");
            SfxToolFixture.Success(fixture.Tools.CreateSfx(destination, "gameboy", "pickup", "候補", dryRun: true));
            Assert.False(File.Exists(destination));
            SfxToolFixture.Success(fixture.Tools.CreateSfx(destination, "gameboy", "pickup", "候補"));
            Song candidate = SongSerializer.Load(destination);
            Assert.Equal("候補", candidate.Title);
            Assert.Equal("coin", candidate.Sfx!.Known!.SourcePreset);
            Assert.Equal("既存の側車", File.ReadAllText(sidecar));
            SfxToolFixture.Failure(fixture.Tools.CreateSfx(destination), 1, "DestinationExists");
            SfxToolFixture.Failure(fixture.Tools.CreateSfx(currentPath, dryRun: true), 1, "DestinationExists");
            SfxToolFixture.Failure(fixture.Tools.CreateSfx(fixture.DirectoryPath), 1, "DestinationExists");
            SfxToolFixture.Failure(fixture.Tools.CreateSfx(" "), 1, "InvalidParameter", "path");
            SfxToolFixture.Failure(fixture.Tools.CreateSfx(fixture.PathFor("bad.json"), preset: "unknown"), 1, "InvalidParameter", "preset");
            SfxToolFixture.Failure(fixture.Tools.CreateSfx(fixture.PathFor("missing/child.json")), 3, "InputOutputError", "path");
            Assert.Same(original, fixture.Session.Song);
            Assert.Equal(currentPath, fixture.Session.Path);
            Assert.Equal(information, fixture.Tools.SongInfo());
            Assert.Equal(bytes, File.ReadAllBytes(currentPath));
            Assert.Equal(1, fixture.Session.History.UndoCount);
            Assert.Equal(1, fixture.Session.History.RedoCount);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
            SfxToolFixture.Success(fixture.Tools.Redo());
            Assert.Equal(550, fixture.Session.Song!.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
        }
    }
}
