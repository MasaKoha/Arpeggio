using System.IO;
using System.Text.Json;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Mcp.Sfx;
using Arpeggio.Mcp.Brief;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Brief
{
    /// <summary>MCP の作曲指示書操作と、成功・失敗時の現セッション維持を固定する。</summary>
    public sealed class BriefToolsTests
    {
        /// <summary>未オープンでも全項目の作成・編集・表示・テキスト取得ができる。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes)]
        [InlineData("gameboy", ChipKind.GameBoy)]
        [InlineData("snes", ChipKind.Snes)]
        public void AllToolsWorkWithoutOpeningSong(string chipName, ChipKind chip)
        {
            using var fixture = new SfxToolFixture();
            var tools = new McpBriefTools(fixture.Session);
            string path = fixture.PathFor("song.brief.json");
            JsonElement created = SfxToolFixture.Success(tools.CreateBrief(path, chip: chipName));
            Assert.Equal("create", created.GetProperty("operation").GetString());
            Assert.Equal(CompositionBrief.DefaultTitle, CompositionBriefFile.Load(path).Title);
            JsonElement edited = SfxToolFixture.Success(tools.TweakBrief(path, title: "廃墟の朝", chip: chipName,
                tempo: 96, mood: "静か\n希望", structure: "導入\r\n主題", instrumentation: "主旋律 / 低音",
                references: "参考曲", constraints: "ループ", notes: "  補足  "));
            var expected = new CompositionBrief
            {
                Title = "廃墟の朝", Chip = chip, TempoBpm = 96, Mood = "静か\n希望", Structure = "導入\r\n主題",
                Instrumentation = "主旋律 / 低音", References = "参考曲", Constraints = "ループ", Notes = "  補足  "
            };
            Assert.Equal("tweak", edited.GetProperty("operation").GetString());
            Assert.Equal(Path.GetFullPath(path), edited.GetProperty("path").GetString());
            Assert.Equal(expected, CompositionBriefFile.Load(path));
            byte[] before = File.ReadAllBytes(path);
            Assert.Equal(CompositionBriefFile.Serialize(expected), tools.ShowBrief(path));
            Assert.Equal(CompositionBriefTextRenderer.Render(expected), tools.BriefText(path));
            Assert.Equal(before, File.ReadAllBytes(path));
            SfxToolFixture.Success(tools.TweakBrief(path, notes: "", clearChip: true, clearTempo: true));
            Assert.Equal(expected with { Chip = null, TempoBpm = null, Notes = string.Empty }, CompositionBriefFile.Load(path));
            Assert.Null(fixture.Session.Song);
            Assert.Null(fixture.Session.Path);
            Assert.Equal(0, fixture.Session.History.UndoCount);
            Assert.Equal(0, fixture.Session.History.RedoCount);
            Assert.False(Directory.Exists(path + ".history"));
        }

        /// <summary>入力エラーでは正常な他項目も保存せず、コード・位置を返す。</summary>
        [Fact]
        public void InvalidInputDoesNotPartiallySave()
        {
            using var fixture = new SfxToolFixture();
            var tools = new McpBriefTools(fixture.Session);
            string path = fixture.PathFor("song.brief.json");
            SfxToolFixture.Success(tools.CreateBrief(path, "元の曲"));
            byte[] before = File.ReadAllBytes(path);
            SfxToolFixture.Failure(tools.TweakBrief(path, title: ""), 1, "InvalidParameter", "title");
            SfxToolFixture.Failure(tools.TweakBrief(path, title: "変更", chip: "none"), 1, "InvalidParameter", "chip");
            SfxToolFixture.Failure(tools.TweakBrief(path, title: "変更", tempo: 0), 1, "InvalidParameter", "tempoBpm");
            SfxToolFixture.Failure(tools.TweakBrief(path, chip: "nes", clearChip: true), 1, "InvalidParameter", "chip");
            SfxToolFixture.Failure(tools.TweakBrief(path, tempo: 120, clearTempo: true), 1, "InvalidParameter", "tempoBpm");
            SfxToolFixture.Failure(tools.TweakBrief(path), 1, "InvalidParameter", "arguments");
            SfxToolFixture.Failure(tools.TweakBrief(path, title: "変更", notes: new string('あ', CompositionBriefValidator.MaximumTextLength + 1)),
                1, "InvalidParameter", "notes");
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>作成拒否・不正文書・I/O を CLI と同じ分類で返す。</summary>
        [Fact]
        public void FailuresUseTheSameBoundaryAsCli()
        {
            using var fixture = new SfxToolFixture();
            var tools = new McpBriefTools(fixture.Session);
            string path = fixture.PathFor("song.brief.json");
            SfxToolFixture.Success(tools.CreateBrief(path));
            SfxToolFixture.Failure(tools.CreateBrief(path), 1, "DestinationExists", "path");
            SfxToolFixture.Failure(tools.CreateBrief(fixture.DirectoryPath), 1, "DestinationExists", "path");
            SfxToolFixture.Failure(tools.CreateBrief(" "), 1, "InvalidParameter", "path");
            SfxToolFixture.Failure(tools.CreateBrief(fixture.PathFor("invalid.brief.json"), title: ""), 1, "InvalidParameter", "title");
            SfxToolFixture.Failure(tools.CreateBrief(fixture.PathFor("missing/new.brief.json")), 3, "InputOutputError", "path");
            SfxToolFixture.Failure(tools.ShowBrief(fixture.PathFor("missing.brief.json")), 3, "InputOutputError", "path");
            SfxToolFixture.Failure(tools.BriefText(fixture.PathFor("missing.brief.json")), 3, "InputOutputError", "path");
            SfxToolFixture.Failure(tools.TweakBrief(fixture.PathFor("missing.brief.json"), title: "変更"), 3, "InputOutputError", "path");
            File.WriteAllText(path, "{}");
            JsonElement showFailure = SfxToolFixture.Failure(tools.ShowBrief(path), 2, "InvalidBrief", "version");
            Assert.Equal("show", showFailure.GetProperty("operation").GetString());
            SfxToolFixture.Failure(tools.BriefText(path), 2, "InvalidBrief", "version");
            SfxToolFixture.Failure(tools.TweakBrief(path, title: "変更"), 2, "InvalidBrief", "version");
            Assert.Equal("{}", File.ReadAllText(path));
            Assert.False(File.Exists(fixture.PathFor("invalid.brief.json")));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>全操作の成功・失敗と Song 誤指定でも現曲・保存先・Undo/Redo を保持する。</summary>
        [Fact]
        public void EveryOperationPreservesCurrentSongAndHistory()
        {
            using var fixture = new SfxToolFixture();
            string currentPath = fixture.CreateAndOpen();
            SfxToolFixture.Success(fixture.Tools.TweakSfx("{\"tone\":{\"baseFrequencyHz\":330}}"));
            SfxToolFixture.Success(fixture.Tools.TweakSfx("{\"tone\":{\"baseFrequencyHz\":550}}"));
            SfxToolFixture.Success(fixture.Tools.Undo());
            Song current = fixture.Session.Song!;
            string information = fixture.Tools.SongInfo();
            byte[] bytes = File.ReadAllBytes(currentPath);
            var tools = new McpBriefTools(fixture.Session);
            string path = fixture.PathFor("independent.brief.json");
            SfxToolFixture.Success(tools.CreateBrief(path));
            SfxToolFixture.Success(tools.TweakBrief(path, mood: "静か"));
            SfxToolFixture.Success(tools.ShowBrief(path));
            Assert.Equal(CompositionBriefTextRenderer.Render(CompositionBriefFile.Load(path)), tools.BriefText(path));
            SfxToolFixture.Failure(tools.CreateBrief(currentPath), 1, "DestinationExists", "path");
            SfxToolFixture.Failure(tools.TweakBrief(currentPath, title: "誤操作"), 2, "InvalidBrief");
            SfxToolFixture.Failure(tools.ShowBrief(currentPath), 2, "InvalidBrief");
            SfxToolFixture.Failure(tools.BriefText(currentPath), 2, "InvalidBrief");
            SfxToolFixture.Failure(tools.TweakBrief(path, title: ""), 1, "InvalidParameter", "title");
            Assert.Same(current, fixture.Session.Song);
            Assert.Equal(currentPath, fixture.Session.Path);
            Assert.Equal(information, fixture.Tools.SongInfo());
            Assert.Equal(bytes, File.ReadAllBytes(currentPath));
            Assert.Equal(1, fixture.Session.History.UndoCount);
            Assert.Equal(1, fixture.Session.History.RedoCount);
            SfxToolFixture.Success(fixture.Tools.Redo());
            Assert.Equal(550, fixture.Session.Song!.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
        }
    }
}
