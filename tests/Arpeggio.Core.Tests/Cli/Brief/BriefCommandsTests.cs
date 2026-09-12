using System.IO;
using System.Text.Json;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Brief
{
    /// <summary>作曲指示書 CLI の作成・部分編集・二種類の表示を固定する。</summary>
    [Collection("Cli")]
    public sealed class BriefCommandsTests
    {
        /// <summary>各チップで独立文書を作成し、全項目を一括編集できる。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes)]
        [InlineData("gameboy", ChipKind.GameBoy)]
        [InlineData("snes", ChipKind.Snes)]
        public void CreateAndTweakEveryField(string chipName, ChipKind chip)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("song.brief.json");
            JsonElement created = SuccessJson("brief", "create", path, "--title", "仮題", "--chip", chipName, "--json");
            Assert.Equal("create", created.GetProperty("operation").GetString());
            Assert.Equal(Path.GetFullPath(path), created.GetProperty("path").GetString());
            Assert.Equal(chip, CompositionBriefFile.Load(path).Chip);
            JsonElement edited = SuccessJson("brief", "tweak", path,
                "--title", "廃墟の朝", "--chip", chipName, "--tempo", "96", "--mood", "静か\n希望",
                "--structure", "導入\r\n主題", "--instrumentation", "主旋律 / 低音", "--references", "参考曲",
                "--constraints", "ループ", "--notes", "  補足  ", "--json");
            Assert.Equal("tweak", edited.GetProperty("operation").GetString());
            var expected = new CompositionBrief
            {
                Title = "廃墟の朝", Chip = chip, TempoBpm = 96, Mood = "静か\n希望", Structure = "導入\r\n主題",
                Instrumentation = "主旋律 / 低音", References = "参考曲", Constraints = "ループ", Notes = "  補足  "
            };
            Assert.Equal(expected, CompositionBriefFile.Load(path));
            Assert.Equal(expected, CompositionBriefFile.Deserialize(edited.GetProperty("brief").GetRawText()));
            Assert.False(Directory.Exists(path + ".history"));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>省略項目は維持し、明示した空文字と clear フラグだけが解除される。</summary>
        [Fact]
        public void PartialTweakPreservesUnspecifiedFieldsAndClearsSelectedValues()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("song.brief.json");
            var original = new CompositionBrief
            {
                Title = "曲", Chip = ChipKind.Nes, TempoBpm = 120,
                Mood = "雰囲気", Structure = "構成", Instrumentation = "役割", References = "参考", Constraints = "制約", Notes = "メモ"
            };
            CompositionBriefFile.Create(original, path);
            SuccessJson("brief", "tweak", path, "--notes", "", "--clear-chip", "--clear-tempo", "--json");
            Assert.Equal(original with { Chip = null, TempoBpm = null, Notes = string.Empty }, CompositionBriefFile.Load(path));
            SuccessJson("brief", "tweak", path, "--chip", "gameboy", "--tempo", "1", "--json");
            CompositionBrief edited = CompositionBriefFile.Load(path);
            Assert.Equal(ChipKind.GameBoy, edited.Chip);
            Assert.Equal(CompositionBriefValidator.MinimumTempoBpm, edited.TempoBpm);
            Assert.Equal(original.Mood, edited.Mood);
        }

        /// <summary>既定作成後の show は JSON と本文を分け、text は余計な出力を混ぜない。</summary>
        [Fact]
        public void ShowAndTextUseCoreOutputWithoutChangingFile()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("song.brief.json");
            var created = CliConversionFixture.Invoke("brief", "create", path);
            Assert.Equal(0, created.ExitCode);
            Assert.Empty(created.Error);
            Assert.Equal(Path.GetFullPath(path), created.Output.Trim());
            CompositionBrief brief = CompositionBriefFile.Load(path);
            Assert.Equal(CompositionBrief.DefaultTitle, brief.Title);
            byte[] before = File.ReadAllBytes(path);
            JsonElement shown = SuccessJson("brief", "show", path, "--json");
            Assert.Equal(brief, CompositionBriefFile.Deserialize(shown.GetRawText()));
            string expected = CompositionBriefTextRenderer.Render(brief);
            foreach (string operation in new[] { "show", "text" })
            {
                var result = CliConversionFixture.Invoke("brief", operation, path);
                Assert.Equal(0, result.ExitCode);
                Assert.Empty(result.Error);
                Assert.Equal(expected, result.Output);
            }
            var textWithJson = CliConversionFixture.Invoke("brief", "text", path, "--json");
            Assert.Equal(0, textWithJson.ExitCode);
            Assert.Equal(expected, textWithJson.Output);
            Assert.Empty(textWithJson.Error);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        internal static JsonElement SuccessJson(params string[] arguments)
        {
            var result = CliConversionFixture.Invoke(arguments);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.False(document.RootElement.TryGetProperty("error", out _), result.Output);
            return document.RootElement.Clone();
        }
    }
}
