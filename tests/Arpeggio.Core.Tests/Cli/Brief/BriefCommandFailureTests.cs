using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Brief;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Brief
{
    /// <summary>解析時点の JSON 契約と保存前の一括検証を固定する。</summary>
    [Collection("Cli")]
    public sealed class BriefCommandFailureTests
    {
        /// <summary>全値オプションで後続 --json が飲み込まれても、単一 JSON エラーになり保存しない。</summary>
        [Theory]
        [InlineData("create", "--title")]
        [InlineData("create", "--chip")]
        [InlineData("tweak", "--title")]
        [InlineData("tweak", "--chip")]
        [InlineData("tweak", "--tempo")]
        [InlineData("tweak", "--mood")]
        [InlineData("tweak", "--structure")]
        [InlineData("tweak", "--instrumentation")]
        [InlineData("tweak", "--references")]
        [InlineData("tweak", "--constraints")]
        [InlineData("tweak", "--notes")]
        public void MissingValuesDoNotConsumeFollowingOptions(string operation, string option)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.PathFor("source.brief.json");
            CompositionBriefFile.Create(new CompositionBrief { Title = "元の曲" }, source);
            string destination = operation == "create" ? fixture.PathFor("new.brief.json") : source;
            byte[] before = File.ReadAllBytes(source);
            AssertFailure(1, "InvalidParameter", "arguments", "brief", operation, destination, option);
            AssertFailure(1, "InvalidParameter", "arguments", "brief", operation, destination, option, "--unknown");
            Assert.Equal(before, File.ReadAllBytes(source));
            if (operation == "create")
            {
                Assert.False(File.Exists(destination));
            }
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>必須位置引数の不足と未知引数も統一した終了コードになる。</summary>
        [Theory]
        [InlineData("create")]
        [InlineData("tweak")]
        [InlineData("show")]
        [InlineData("text")]
        [InlineData("show --unknown")]
        [InlineData("unknown")]
        public void ParserFailuresReturnOneJson(string command)
        {
            AssertFailure(1, "InvalidParameter", "arguments", new[] { "brief" }.Concat(command.Split(' ')).ToArray());
        }

        /// <summary>不正な値やクリアとの競合は他の正常な編集も含めて保存しない。</summary>
        [Theory]
        [InlineData("--title", "", "title")]
        [InlineData("--title", " ", "title")]
        [InlineData("--chip", "none", "chip")]
        [InlineData("--chip", "unknown", "chip")]
        [InlineData("--tempo", "0", "tempoBpm")]
        [InlineData("--tempo", "-1", "tempoBpm")]
        [InlineData("--tempo", "1.5", "arguments")]
        [InlineData("--tempo", "2147483648", "arguments")]
        public void InvalidValuesKeepOriginalBytes(string option, string value, string parameterPath)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("song.brief.json");
            CompositionBriefFile.Create(new CompositionBrief { Title = "元の曲" }, path);
            byte[] before = File.ReadAllBytes(path);
            AssertFailure(1, "InvalidParameter", parameterPath, "brief", "tweak", path, "--notes", "先に変更", option, value);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        /// <summary>重複・無指定・長すぎる値・設定解除の併用を拒否する。</summary>
        [Fact]
        public void ConflictingAndEmptyEditsAreRejectedAtomically()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("song.brief.json");
            CompositionBriefFile.Create(new CompositionBrief { Title = "元の曲" }, path);
            byte[] before = File.ReadAllBytes(path);
            AssertFailure(1, "InvalidParameter", "chip", "brief", "tweak", path, "--chip", "nes", "--clear-chip");
            AssertFailure(1, "InvalidParameter", "tempoBpm", "brief", "tweak", path, "--tempo", "120", "--clear-tempo");
            AssertFailure(1, "InvalidParameter", "arguments", "brief", "tweak", path);
            AssertFailure(1, "InvalidParameter", "arguments", "brief", "tweak", path, "--title", "先", "--title", "後");
            AssertFailure(1, "InvalidParameter", "notes", "brief", "tweak", path, "--title", "変更", "--notes",
                new string('あ', CompositionBriefValidator.MaximumTextLength + 1));
            AssertFailure(1, "InvalidParameter", "title", "brief", "tweak", path, "--title",
                new string('あ', CompositionBriefValidator.MaximumTitleLength + 1));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(Directory.Exists(path + ".history"));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>既存宛先・文書不正・入出力の失敗を分類し、非 JSON 時は標準エラーだけへ出す。</summary>
        [Fact]
        public void ExitCodesAndOutputChannelsDistinguishFailures()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("song.brief.json");
            CompositionBriefFile.Create(new CompositionBrief { Title = "元の曲" }, path);
            byte[] before = File.ReadAllBytes(path);
            AssertFailure(1, "DestinationExists", "path", "brief", "create", path);
            Assert.Equal(before, File.ReadAllBytes(path));
            string songPath = fixture.CreateSong();
            byte[] songBytes = File.ReadAllBytes(songPath);
            AssertFailure(2, "InvalidBrief", "ticksPerBeat", "brief", "tweak", songPath, "--title", "誤操作");
            Assert.Equal(songBytes, File.ReadAllBytes(songPath));
            AssertFailure(3, "InputOutputError", "path", "brief", "create", fixture.PathFor("missing/song.brief.json"));
            foreach (string operation in new[] { "show", "text", "tweak" })
            {
                string[] arguments = { "brief", operation, fixture.PathFor("missing.brief.json") };
                if (operation == "tweak")
                {
                    arguments = arguments.Concat(new[] { "--title", "変更" }).ToArray();
                }
                AssertFailure(3, "InputOutputError", "path", arguments);
            }
            File.WriteAllText(path, "{}");
            AssertFailure(2, "InvalidBrief", "version", "brief", "show", path);
            AssertFailure(2, "InvalidBrief", "version", "brief", "text", path);
            AssertFailure(2, "InvalidBrief", "version", "brief", "tweak", path, "--title", "変更");
            Assert.Equal("{}", File.ReadAllText(path));
            var plain = CliConversionFixture.Invoke("brief", "show", path);
            Assert.Equal(2, plain.ExitCode);
            Assert.Empty(plain.Output);
            Assert.Contains("InvalidBrief", plain.Error);
        }

        private static void AssertFailure(int exitCode, string code, string parameterPath, params string[] arguments)
        {
            var result = CliConversionFixture.Invoke(arguments.Concat(new[] { "--json" }).ToArray());
            Assert.Equal(exitCode, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement root = document.RootElement;
            Assert.Equal(exitCode, root.GetProperty("exitCode").GetInt32());
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.Equal(parameterPath, root.GetProperty("parameterPath").GetString());
            Assert.Equal(arguments[1], root.GetProperty("operation").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        }
    }
}
