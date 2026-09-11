using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Sfx
{
    /// <summary>全編集操作の予行・履歴失敗・revision競合・同期修復を検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxTransactionCommandsTests
    {
        /// <summary>各操作の dry-run は元バイト・履歴・日時を維持し、候補と実適用の revision が一致する。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("randomize")]
        [InlineData("mutate")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public void DryRunAndApplyShareOneCandidate(string operation)
        {
            using var fixture = new CliConversionFixture();
            string path = Prepare(fixture, operation);
            byte[] before = File.ReadAllBytes(path);
            byte[] history = File.ReadAllBytes(path + ".history/state.json");
            string revision = SfxHash.ComputeRevision(SongSerializer.Load(path));
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            JsonElement preview = SfxParameterCommandsTests.AssertSuccess(Arguments(path, operation).Concat(new[] { "--dry-run" }).ToArray());
            Assert.True(preview.GetProperty("changed").GetBoolean());
            Assert.Equal(revision, preview.GetProperty("revision").GetString());
            Assert.NotEqual(revision, preview.GetProperty("candidateRevision").GetString());
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(history, File.ReadAllBytes(path + ".history/state.json"));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
            JsonElement applied = SfxParameterCommandsTests.AssertSuccess(Arguments(path, operation)
                .Concat(new[] { "--expected-revision", revision }).ToArray());
            Assert.Equal(preview.GetProperty("candidateRevision").GetString(), applied.GetProperty("revision").GetString());
            SfxExplorationCommandsTests.AssertHistory(path, 1, 0);
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
        }

        /// <summary>履歴保存失敗で、改行を含む元ファイルを全バイト復元する。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("randomize")]
        [InlineData("mutate")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public void HistoryFailureRestoresOriginalBytes(string operation)
        {
            using var fixture = new CliConversionFixture();
            string path = Prepare(fixture, operation);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n") + "\r\n");
            Directory.Delete(path + ".history", true);
            File.WriteAllText(path + ".history", "元の側車状態");
            byte[] before = File.ReadAllBytes(path);
            SfxCommandFailureTests.AssertFailure(3, "InputOutputError", Arguments(path, operation));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal("元の側車状態", File.ReadAllText(path + ".history"));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
        }

        /// <summary>外部title更新も競合として検出し、通常実行と予行を同じ契約で拒否する。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("randomize")]
        [InlineData("mutate")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public void RevisionConflictNeverOverwritesExternalChange(string operation)
        {
            using var fixture = new CliConversionFixture();
            string path = Prepare(fixture, operation);
            string revision = SfxParameterCommandsTests.AssertSuccess("sfx", "params", path).GetProperty("revision").GetString()!;
            Song external = SongSerializer.Load(path);
            external.Title = "外部更新";
            SongSerializer.Save(external, path);
            byte[] before = File.ReadAllBytes(path);
            byte[] history = File.ReadAllBytes(path + ".history/state.json");
            string[] arguments = Arguments(path, operation).Concat(new[] { "--expected-revision", revision }).ToArray();
            SfxCommandFailureTests.AssertFailure(1, "RevisionConflict", arguments);
            SfxCommandFailureTests.AssertFailure(1, "RevisionConflict", arguments.Concat(new[] { "--dry-run" }).ToArray());
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(history, File.ReadAllBytes(path + ".history/state.json"));
        }

        /// <summary>同期不一致は保存意図だけを表示し、明示再生成と Undo で手動編集を復元できる。</summary>
        [Fact]
        public void RegenerateRepairsBothHashesAndDetachAcceptsUnknownVersion()
        {
            using var fixture = new CliConversionFixture();
            string path = Prepare(fixture, "regenerate");
            Song changed = SongSerializer.Load(path);
            SfxDefinitionData definition = changed.Sfx!.Known!;
            changed.Sfx = new SfxDefinition(definition with
            {
                Parameters = definition.Parameters with { Tone = definition.Parameters.Tone with { BaseFrequencyHz = 660 } }
            });
            SongSerializer.Save(changed, path);
            byte[] manualBytes = File.ReadAllBytes(path);
            JsonElement parameters = SfxParameterCommandsTests.AssertSuccess("sfx", "params", path);
            Assert.False(parameters.GetProperty("editable").GetBoolean());
            Assert.Equal("SavedParametersChanged", parameters.GetProperty("reason").GetString());
            Assert.Equal(2, parameters.GetProperty("reasons").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, parameters.GetProperty("parameters").ValueKind);
            Assert.Equal(JsonValueKind.Null, parameters.GetProperty("generation").ValueKind);
            Assert.Equal(660, parameters.GetProperty("savedParameters").GetProperty("tone").GetProperty("baseFrequencyHz").GetDouble());
            SfxCommandFailureTests.AssertFailure(1, "SavedParametersChanged", Arguments(path, "randomize"));
            JsonElement regenerated = SfxParameterCommandsTests.AssertSuccess(Arguments(path, "regenerate"));
            Assert.True(regenerated.GetProperty("editable").GetBoolean());
            Assert.Equal(5, regenerated.GetProperty("replacement").GetProperty("trackCount").GetInt32());
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            Assert.Equal(manualBytes, File.ReadAllBytes(path));
            SongSerializer.Save(SfxEditFixture.CreateUnsupportedSong("generatorVersion"), path);
            byte[] unsupported = File.ReadAllBytes(path);
            SfxCommandFailureTests.AssertFailure(1, "UnsupportedSfxVersion", Arguments(path, "regenerate"));
            SfxParameterCommandsTests.AssertSuccess(Arguments(path, "detach"));
            Assert.Null(SongSerializer.Load(path).Sfx);
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            Assert.Equal(unsupported, File.ReadAllBytes(path));
        }

        /// <summary>予行と同値編集は側車が存在しない場合も新規ディレクトリを作らない。</summary>
        [Fact]
        public void PreviewAndNoOpDoNotCreateMissingHistory()
        {
            using var fixture = new CliConversionFixture();
            string path = Prepare(fixture, "tweak");
            Directory.Delete(path + ".history", true);
            SfxParameterCommandsTests.AssertSuccess("sfx", "mutate", path, "--seed", "1", "--dry-run");
            SfxParameterCommandsTests.AssertSuccess("sfx", "mutate", path, "--seed", "1", "--strength", "0");
            SfxParameterCommandsTests.AssertSuccess("sfx", "tweak", path, "--frequency", "196");
            Assert.False(Directory.Exists(path + ".history"));
        }

        private static string Prepare(CliConversionFixture fixture, string operation)
        {
            string path = fixture.PathFor("source.json");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path);
            if (operation == "regenerate")
            {
                Song manual = SongSerializer.Load(path);
                manual.Tracks[0].Notes[0].Volume = 3;
                SongSerializer.Save(manual, path);
            }
            return path;
        }

        private static string[] Arguments(string path, string operation)
        {
            string[] options = operation switch
            {
                "tweak" => new[] { "--frequency", "660" },
                "randomize" => new[] { "--category", "hit", "--seed", "1" },
                "mutate" => new[] { "--seed", "1", "--strength", "0.5" },
                "regenerate" => new[] { "--replace-generated" },
                _ => Array.Empty<string>()
            };
            return new[] { "sfx", operation, path }.Concat(options).ToArray();
        }
    }
}
