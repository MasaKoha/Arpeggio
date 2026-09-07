using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Arpeggio.Cli;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Tests.Import;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>WAV 取り込みと OGG 書き出しの公開 CLI 契約・履歴・短い表示を検証する。</summary>
    [Collection("Cli")]
    public sealed class SampleCodecCommandsTests
    {
        /// <summary>取り込みを一履歴で保存し、一覧と show から Base64 を除き、undo/redo できる。</summary>
        [Fact]
        public void ImportWav_SavesSampleWithHistoryAndConciseDisplay()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { 0, short.MaxValue, short.MinValue, 0 });
            RequireSuccess("new", files.SongPath, "--chip", "snes");
            RequireSuccess("instrument", "import-wav", files.SongPath, "--id", "1", files.WavePath,
                "--root", "C5", "--loop-start", "1", "--loop-end", "3");
            SnesSampleInstrument sample = GetSample(files.SongPath);
            Assert.Equal(72, sample.RootMidiNote);
            Assert.Equal(1, sample.LoopStart);
            Assert.Equal(3, sample.LoopEnd);
            Assert.True(sample.Loop);
            const string Summary = "sample 4 smp @ 22050 Hz";
            string list = RequireSuccess("instrument", "list", files.SongPath);
            string jsonList = RequireSuccess("instrument", "list", files.SongPath, "--json");
            string show = RequireSuccess("show", files.SongPath);
            string jsonShow = RequireSuccess("show", files.SongPath, "--json");
            foreach (string output in new[] { list, jsonList, show, jsonShow })
            {
                Assert.Contains(Summary, output);
                Assert.DoesNotContain(sample.SampleData!, output);
            }
            using JsonDocument listing = JsonDocument.Parse(jsonList);
            Assert.False(listing.RootElement[0].TryGetProperty("sampleData", out _));
            RequireSuccess("undo", files.SongPath);
            Assert.Null(GetSample(files.SongPath).SampleData);
            RequireSuccess("redo", files.SongPath);
            Assert.Equal(sample.SampleData, GetSample(files.SongPath).SampleData);
            RequireSuccess("instrument", "import-wav", files.SongPath, "--id", "1", files.WavePath, "--no-loop");
            Assert.False(GetSample(files.SongPath).Loop);
            Assert.Equal(60, GetSample(files.SongPath).RootMidiNote);
        }

        /// <summary>取り込み失敗で曲と側車履歴を保持し、不正 ID・種類・ファイルを分類する。</summary>
        [Fact]
        public void ImportWav_FailurePreservesSongAndHistory()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { 1, 2 });
            RequireSuccess("new", files.SongPath, "--chip", "snes");
            string before = File.ReadAllText(files.SongPath);
            string historyPath = Path.Combine(files.SongPath + ".history", "state.json");
            string historyBefore = File.ReadAllText(historyPath);
            Assert.Equal(2, Invoke("instrument", "import-wav", files.SongPath, "--id", "1", files.WavePath, "--loop-start", "2").ExitCode);
            Assert.Equal(1, Invoke("instrument", "import-wav", files.SongPath, "--id", "999", files.WavePath).ExitCode);
            Assert.Equal(1, Invoke("instrument", "import-wav", files.SongPath, "--id", "1", files.WavePath, "--root", "bad").ExitCode);
            Assert.Equal(3, Invoke("instrument", "import-wav", files.SongPath, "--id", "1", files.WavePath + ".missing").ExitCode);
            File.WriteAllText(files.WavePath, "broken WAV");
            Assert.Equal(1, Invoke("instrument", "import-wav", files.SongPath, "--id", "1", files.WavePath).ExitCode);
            Assert.Equal(before, File.ReadAllText(files.SongPath));
            Assert.Equal(historyBefore, File.ReadAllText(historyPath));
            string nesPath = Path.Combine(files.DirectoryPath, "nes.json");
            RequireSuccess("new", nesPath, "--chip", "nes");
            Assert.Equal(1, Invoke("instrument", "import-wav", nesPath, "--id", "1", files.WavePath).ExitCode);
        }

        /// <summary>WAV と共通のオプション・警告を使い、OGG でより小さなファイルを保存する。</summary>
        [Fact]
        public void ExportOgg_UsesRenderOptionsAndPreservesDocument()
        {
            using SampleFileFixture files = new SampleFileFixture();
            RequireSuccess("new", files.SongPath, "--chip", "snes", "--length-beats", "4");
            RequireSuccess("note", "add", files.SongPath, "--track", "0", "--tick", "0", "--duration", "192", "--note", "C8");
            string before = File.ReadAllText(files.SongPath);
            string output = RequireSuccess("export", "ogg", files.SongPath, files.OggPath, "--loops", "2", "--sample-rate", "22050",
                "--tail", "0.25", "--quality", "0.4", "--json");
            RequireSuccess("export", "wav", files.SongPath, files.WavePath, "--loops", "2", "--sample-rate", "22050", "--tail", "0.25");
            Assert.Equal("OggS", Encoding.ASCII.GetString(File.ReadAllBytes(files.OggPath), 0, 4));
            Assert.True(new FileInfo(files.OggPath).Length < new FileInfo(files.WavePath).Length);
            using JsonDocument document = JsonDocument.Parse(output);
            Assert.NotEqual(0, document.RootElement.GetProperty("warnings").GetArrayLength());
            Assert.Equal(before, File.ReadAllText(files.SongPath));
            byte[] encoded = File.ReadAllBytes(files.OggPath);
            Assert.Equal(1, Invoke("export", "ogg", files.SongPath, files.OggPath, "--quality", "2").ExitCode);
            Assert.Equal(1, Invoke("export", "ogg", files.SongPath, files.OggPath, "--loops", "0").ExitCode);
            Assert.Equal(1, Invoke("export", "ogg", files.SongPath, files.OggPath, "--sample-rate", "0").ExitCode);
            Assert.Equal(encoded, File.ReadAllBytes(files.OggPath));
        }

        private static SnesSampleInstrument GetSample(string path) => (SnesSampleInstrument)SongSerializer.Load(path).Instruments[0];

        private static string RequireSuccess(params string[] arguments)
        {
            (int exitCode, string output, string error) = Invoke(arguments);
            Assert.True(exitCode == 0, error);
            return output;
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
    }
}
