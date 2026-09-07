using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Cli;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>プロセスを起動せず CLI 全体の入出力と永続履歴を検証する。</summary>
    [Collection("Cli")]
    public sealed class CliExecutionTests
    {
        /// <summary>各チップの新規作成と情報取得がつながる。</summary>
        [Theory]
        [InlineData("nes", 5)]
        [InlineData("gameboy", 4)]
        [InlineData("snes", 8)]
        public void CreatesEachChip(string chip, int trackCount)
        {
            WithDirectory(directory =>
            {
                string path = Path.Combine(directory, "song.json");
                Assert.Equal(0, Invoke("new", path, "--chip", chip, "--tempo", "120", "--length-beats", "4", "--title", "test").ExitCode);
                using JsonDocument information = JsonDocument.Parse(Invoke("info", path, "--json").Output);
                JsonElement root = information.RootElement;
                Assert.Equal("test", root.GetProperty("title").GetString());
                Assert.Equal(120, root.GetProperty("tempoBpm").GetInt32());
                Assert.Equal(192, root.GetProperty("lengthTicks").GetInt32());
                Assert.Equal(trackCount, root.GetProperty("tracks").GetArrayLength());
                Assert.Equal(0, Invoke("chip-reference", chip).ExitCode);
            });
        }

        /// <summary>四音の NES メロディーを独立した RIFF 読み取りで非無音と確認する。</summary>
        [Fact]
        public void FourNotesExportNonSilentStereoWav()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                string[] notes = { "C5", "E5", "G5", "C6" };
                for (int index = 0; index < notes.Length; index++)
                {
                    Assert.Equal(0, Invoke("note", "add", path, "--track", "0", "--tick", (index * 24).ToString(),
                        "--duration", "24", "--note", notes[index]).ExitCode);
                }
                string output = Path.Combine(directory, "song.wav");
                Assert.Equal(0, Invoke("export", "wav", path, output, "--loops", "1", "--sample-rate", "22050", "--tail", "0").ExitCode);
                using FileStream stream = File.OpenRead(output);
                WavReader reader = new WavReader(stream);
                Assert.Equal(22050, reader.SampleRate);
                Assert.Equal(2, reader.Channels);
                Assert.Equal(16, reader.BitsPerSample);
                Assert.Contains(reader.Samples, sample => sample != 0);
                Assert.Contains("C-5 15 01", Invoke("show", path).Output);
                Assert.Contains("...", Invoke("show", path).Output);
                Assert.Contains("---", Invoke("show", path).Output);
            });
        }

        /// <summary>移動・長さ変更・削除と別呼び出し間の undo/redo が保存される。</summary>
        [Fact]
        public void NoteCommandsAndPersistentHistory()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                Assert.Equal(0, Invoke("note", "add", path, "--track", "0", "--tick", "0", "--duration", "24", "--note", "Db5",
                    "--effect", "PitchSlide=-4", "Vibrato=20", "--effect", "Arpeggio=0x47").ExitCode);
                Assert.Equal(0, Invoke("note", "move", path, "--track", "0", "--tick", "0", "--to-tick", "24", "--note", "60").ExitCode);
                Assert.Equal(0, Invoke("note", "resize", path, "--track", "0", "--tick", "24", "--duration", "12").ExitCode);
                Note note = Assert.Single(SongSerializer.Load(path).Tracks[0].Notes);
                Assert.Equal(60, note.MidiNote);
                Assert.Equal(12, note.DurationTicks);
                Assert.Equal(3, note.Effects.Length);
                Assert.Contains("*", Invoke("show", path, "--track", "0", "--from-tick", "24", "--to-tick", "48").Output);
                using JsonDocument shown = JsonDocument.Parse(Invoke("show", path, "--track", "0", "--json").Output);
                Assert.Equal(1, shown.RootElement.GetProperty("tracks").GetArrayLength());
                Assert.Equal(0, Invoke("note", "remove", path, "--track", "0", "--tick", "24").ExitCode);
                Assert.Empty(SongSerializer.Load(path).Tracks[0].Notes);
                Assert.Equal(0, Invoke("undo", path).ExitCode);
                Assert.Single(SongSerializer.Load(path).Tracks[0].Notes);
                Assert.Equal(0, Invoke("redo", path).ExitCode);
                Assert.Empty(SongSerializer.Load(path).Tracks[0].Notes);
            });
        }

        /// <summary>バッチは一回で undo でき、途中失敗は曲・永続履歴とも不変。</summary>
        [Fact]
        public void BatchIsOnePersistentHistoryAndFailureRollsBack()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                string operations = Path.Combine(directory, "operations.json");
                File.WriteAllText(operations, "[{\"kind\":\"AddNote\",\"tick\":0,\"durationTicks\":24,\"midiNote\":60},{\"kind\":\"AddNote\",\"tick\":24,\"durationTicks\":24,\"midiNote\":64}]");
                Assert.Equal(0, Invoke("apply", path, "--operations", operations, "--track", "0").ExitCode);
                Assert.Equal(2, SongSerializer.Load(path).Tracks[0].Notes.Count);
                Assert.Equal(0, Invoke("undo", path).ExitCode);
                Assert.Empty(SongSerializer.Load(path).Tracks[0].Notes);
                Assert.Equal(1, Invoke("undo", path).ExitCode);
                Assert.Equal(0, Invoke("redo", path).ExitCode);
                string before = File.ReadAllText(path);
                string historyBefore = File.ReadAllText(path + ".history/state.json");
                File.WriteAllText(operations, "[{\"kind\":\"RemoveNote\",\"track\":0,\"tick\":0},{\"kind\":\"RemoveNote\",\"track\":0,\"tick\":999}]");
                Assert.Equal(1, Invoke("apply", path, "--operations", operations).ExitCode);
                Assert.Equal(before, File.ReadAllText(path));
                Assert.Equal(historyBefore, File.ReadAllText(path + ".history/state.json"));
            });
        }

        /// <summary>標準入力からも JSON バッチを適用できる。</summary>
        [Fact]
        public void ReadsBatchFromStandardInput()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                TextReader original = Console.In;
                using StringReader input = new StringReader("[{\"kind\":\"SetTempo\",\"tempoBpm\":180}]");
                try
                {
                    Console.SetIn(input);
                    Assert.Equal(0, Invoke("apply", path, "--operations", "-").ExitCode);
                }
                finally
                {
                    Console.SetIn(original);
                }
                Assert.Equal(180, SongSerializer.Load(path).TempoBpm);
            });
        }

        /// <summary>警告は通常時 stderr、JSON 時 warnings に出し、成功コードを保つ。</summary>
        [Fact]
        public void ExportWarningsDoNotFail()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                Assert.Equal(0, Invoke("note", "add", path, "--track", "0", "--tick", "0", "--duration", "24", "--note", "0").ExitCode);
                string output = Path.Combine(directory, "warning.wav");
                (int ExitCode, string Output, string Error) normal = Invoke("export", "wav", path, output);
                Assert.Equal(0, normal.ExitCode);
                Assert.NotEmpty(normal.Error);
                (int ExitCode, string Output, string Error) json = Invoke("export", "wav", path, output, "--json");
                Assert.Equal(0, json.ExitCode);
                Assert.Empty(json.Error);
                using JsonDocument document = JsonDocument.Parse(json.Output);
                Assert.NotEmpty(document.RootElement.GetProperty("warnings").EnumerateArray().ToArray());
            });
        }

        /// <summary>外部変更で履歴を失効させ、引数・文書・I/O を分類する。</summary>
        [Fact]
        public void InvalidatesStaleHistoryAndMapsErrors()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                Assert.Equal(0, Invoke("note", "add", path, "--track", "0", "--tick", "0", "--duration", "24", "--note", "60").ExitCode);
                Song song = SongSerializer.Load(path);
                song.Title = "external";
                SongSerializer.Save(song, path);
                Assert.Equal(1, Invoke("undo", path).ExitCode);
                Assert.Equal("external", SongSerializer.Load(path).Title);
                Assert.Equal(1, Invoke("unknown-command").ExitCode);
                Assert.Equal(1, Invoke("note", "add", path, "--track", "0").ExitCode);
                Assert.Equal(1, Invoke("note", "add", path, "--track", "0", "--tick", "48", "--duration", "24", "--note", "H4").ExitCode);
                Assert.Equal(2, Invoke("note", "add", path, "--track", "0", "--tick", "12", "--duration", "24", "--note", "60").ExitCode);
                string broken = Path.Combine(directory, "broken.json");
                File.WriteAllText(broken, "{}");
                Assert.Equal(2, Invoke("info", broken).ExitCode);
                Assert.Equal(3, Invoke("info", Path.Combine(directory, "missing.json")).ExitCode);
                Assert.Equal(1, Invoke("chip-reference", "invalid").ExitCode);
                Assert.Equal(1, Invoke("new", path, "--chip", "nes").ExitCode);
            });
        }

        /// <summary>履歴出力の I/O 失敗では、先に保存された曲も操作前へ戻す。</summary>
        [Fact]
        public void HistoryWriteFailureRestoresSong()
        {
            WithDirectory(directory =>
            {
                string path = NewSong(directory);
                string before = File.ReadAllText(path);
                Directory.Delete(path + ".history", true);
                File.WriteAllText(path + ".history", "履歴ディレクトリを作れない状態");
                string operations = Path.Combine(directory, "operations.json");
                File.WriteAllText(operations, "[{\"kind\":\"SetTempo\",\"tempoBpm\":180}]");
                Assert.Equal(3, Invoke("apply", path, "--operations", operations).ExitCode);
                Assert.Equal(before, File.ReadAllText(path));
            });
        }

        private static string NewSong(string directory)
        {
            string path = Path.Combine(directory, "song.arpeggio.json");
            Assert.Equal(0, Invoke("new", path, "--chip", "nes", "--length-beats", "4").ExitCode);
            return path;
        }

        private static (int ExitCode, string Output, string Error) Invoke(params string[] arguments)
        {
            TextWriter originalOutput = Console.Out;
            TextWriter originalError = Console.Error;
            using StringWriter output = new StringWriter();
            using StringWriter error = new StringWriter();
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                int exitCode = CliExecution.Run(arguments);
                return (exitCode, output.ToString(), error.ToString());
            }
            finally
            {
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }
        }

        private static void WithDirectory(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
