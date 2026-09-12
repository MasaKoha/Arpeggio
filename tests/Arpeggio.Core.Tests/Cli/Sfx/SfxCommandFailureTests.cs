using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Storage;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Sfx
{
    /// <summary>引数・入力・文書・I/O の単一 JSON 契約と新規作成の原子性を固定する。</summary>
    [Collection("Cli")]
    public sealed class SfxCommandFailureTests
    {
        /// <summary>解析時点の必須不足・未知オプションも stdout 一つの JSON になる。</summary>
        [Theory]
        [InlineData("create")]
        [InlineData("params --unknown")]
        [InlineData("tweak")]
        [InlineData("tweak source.json --tone-enabled")]
        [InlineData("list --editable --json=invalid")]
        public void ParserErrorsReturnOneJson(string command)
        {
            AssertFailure(1, "InvalidParameter", new[] { "sfx" }.Concat(command.Split(' ')).ToArray());
        }

        /// <summary>パラメータ拒否は元ファイルと履歴を一切変更しない。</summary>
        [Theory]
        [InlineData("--tone-enabled", "yes", "InvalidParameter")]
        [InlineData("--volume", "1.0000000000000001", "InvalidParameter")]
        [InlineData("--frequency", "NaN", "InvalidParameter")]
        [InlineData("--frequency", "1,5", "InvalidParameter")]
        [InlineData("--noise-width", "7", "UnsupportedParameter")]
        [InlineData("--noise-mode", "SHORT", "InvalidParameter")]
        public void InvalidOptionsKeepOriginalBytes(string option, string value, string code)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            byte[] before = File.ReadAllBytes(path);
            byte[] history = File.ReadAllBytes(path + ".history/state.json");
            AssertFailure(1, code, "sfx", "tweak", path, option, value);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(history, File.ReadAllBytes(path + ".history/state.json"));
        }

        /// <summary>patch の構造違反と個別指定混在を、部分保存せず拒否する。</summary>
        [Theory]
        [InlineData("{}")]
        [InlineData("{\"tone\":{\"enabled\":null}}")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":880,\"baseFrequencyHz\":660}}")]
        [InlineData("[]")]
        public void InvalidPatchAndMixedInputAreRejected(string patch)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            byte[] before = File.ReadAllBytes(path);
            string patchFile = fixture.PathFor("patch.json");
            File.WriteAllText(patchFile, patch);
            AssertFailure(1, "InvalidParameter", "sfx", "tweak", path, "--patch", patchFile);
            AssertFailure(1, "InvalidParameter", "sfx", "tweak", path, "--patch", patchFile, "--frequency", "880");
            AssertFailure(1, "InvalidParameter", "sfx", "tweak", path, "--frequency", "880", "--frequency", "660");
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        /// <summary>正常0・操作1・元文書2・入力I/O3を区別する。</summary>
        [Fact]
        public void ExitCodesDistinguishDocumentAndInputFailures()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            AssertFailure(1, "DestinationExists", "sfx", "create", path, "--dry-run");
            AssertFailure(3, "InputOutputError", "sfx", "params", fixture.PathFor("missing.json"));
            AssertFailure(3, "InputOutputError", "sfx", "tweak", path, "--patch", fixture.PathFor("missing-patch.json"));
            File.WriteAllText(path, "{}");
            AssertFailure(2, "InvalidSong", "sfx", "tweak", path, "--frequency", "660");
        }

        /// <summary>dry-run は出力を作らず、create は古い側車を継承せず初期化する。</summary>
        [Fact]
        public void CreatePreviewAndStaleHistoryRespectNewDocumentBoundary()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            string historyDirectory = path + ".history";
            Directory.CreateDirectory(historyDirectory);
            string statePath = Path.Combine(historyDirectory, "state.json");
            File.WriteAllText(statePath, "古い側車");
            JsonElement preview = SfxParameterCommandsTests.AssertSuccess("sfx", "create", path, "--dry-run");
            Assert.False(File.Exists(path));
            Assert.Equal("古い側車", File.ReadAllText(statePath));
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("revision").ValueKind);
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            using JsonDocument history = JsonDocument.Parse(File.ReadAllText(statePath));
            Assert.Empty(history.RootElement.GetProperty("undo").EnumerateArray());
            Assert.Empty(history.RootElement.GetProperty("redo").EnumerateArray());
            Assert.Equal(SfxHash.ComputeRevision(SongSerializer.Load(path)), preview.GetProperty("candidateRevision").GetString());
        }

        /// <summary>履歴保存できない create は新規ファイルを除去し、開始前の側車と一時ファイル状態を保つ。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CreateHistoryFailureRollsBackNewFile(bool blockState)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            string historyPath = path + ".history";
            if (blockState)
            {
                Directory.CreateDirectory(Path.Combine(historyPath, "state.json"));
            }
            else
            {
                File.WriteAllText(historyPath, "側車を保持");
            }
            AssertFailure(3, "InputOutputError", "sfx", "create", path);
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
            if (!blockState)
            {
                Assert.Equal("側車を保持", File.ReadAllText(historyPath));
            }
        }

        internal static JsonElement AssertFailure(int exitCode, string code, params string[] arguments)
        {
            var result = CliConversionFixture.Invoke(arguments.Concat(new[] { "--json" }).ToArray());
            Assert.Equal(exitCode, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(exitCode, document.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
            Assert.True(document.RootElement.TryGetProperty("parameterPath", out _));
            return document.RootElement.Clone();
        }
    }
}
