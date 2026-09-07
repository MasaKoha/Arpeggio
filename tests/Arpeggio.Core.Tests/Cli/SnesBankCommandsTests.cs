using System;
using System.IO;
using System.Linq;
using Arpeggio.Cli;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Import;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>SNES バンクの CLI 入力・履歴・デモの音声出力を検証する。</summary>
    [Collection("Cli")]
    public sealed class SnesBankCommandsTests
    {
        /// <summary>一覧の全音色と推奨値、追加・差し替え・明示上書きを確認する。</summary>
        [Fact]
        public void Presets_AddAndSetApplyRecommendationsAndOverrides()
        {
            using SampleFileFixture files = new SampleFileFixture();
            string catalog = RequireSuccess("instrument", "presets", "snes");
            foreach (SnesInstrumentPreset preset in SnesInstrumentCatalog.All)
            {
                Assert.Contains(preset.Name, catalog);
                Assert.Contains(preset.Category, catalog);
                Assert.Contains(preset.Description, catalog);
            }
            Assert.Contains("ADSR", catalog);
            Assert.Contains("root", catalog);
            Assert.Contains("echo", catalog);
            RequireSuccess("new", files.SongPath, "--chip", "snes");
            RequireSuccess("instrument", "add", files.SongPath, "--kind", "SnesSample", "--preset", "strings", "--name", "str");
            SnesSampleInstrument strings = GetInstrument(files.SongPath, 2);
            Assert.Equal("strings", strings.Preset);
            Assert.Equal(SnesInstrumentCatalog.Get("strings").AdsrRegisters, strings.AdsrRegisters);
            RequireSuccess("instrument", "set", files.SongPath, "--id", "2", "--preset", "brass", "--echo-send", "0.7", "--root", "C4");
            SnesSampleInstrument brass = GetInstrument(files.SongPath, 2);
            Assert.Equal("str", brass.Name);
            Assert.Equal("brass", brass.Preset);
            Assert.Equal(SnesInstrumentCatalog.Get("brass").AdsrRegisters, brass.AdsrRegisters);
            Assert.Equal(0.7, brass.EchoSend);
            Assert.Equal(60, brass.RootMidiNote);
            Assert.Contains("SnesSample brass", RequireSuccess("instrument", "list", files.SongPath));
            Assert.Contains("SnesSample brass", RequireSuccess("show", files.SongPath));
            RequireSuccess("instrument", "set", files.SongPath, "--id", "2", "--preset", "piano", "--adsr", "0.1,0.2,0.5,0.1");
            Assert.Null(GetInstrument(files.SongPath, 2).AdsrRegisters);
            Assert.False(GetInstrument(files.SongPath, 2).Loop);
            RequireSuccess("instrument", "set", files.SongPath, "--id", "2", "--adsr-registers", "15,2,6,0", "--loop", "true");
            Assert.Equal(new SnesAdsrRegisters(15, 2, 6, 0), GetInstrument(files.SongPath, 2).AdsrRegisters);
        }

        /// <summary>未知名・別チップへのバンク指定を拒否し、失敗でファイルを変更しない。</summary>
        [Fact]
        public void InvalidPresetOrBank_DoesNotSave()
        {
            using SampleFileFixture files = new SampleFileFixture();
            Assert.NotEqual(0, Invoke("new", files.SongPath, "--chip", "nes", "--bank", "band").ExitCode);
            Assert.False(File.Exists(files.SongPath));
            Assert.NotEqual(0, Invoke("new", files.SongPath, "--chip", "snes", "--bank", "unknown").ExitCode);
            Assert.False(File.Exists(files.SongPath));
            RequireSuccess("new", files.SongPath, "--chip", "snes");
            string before = File.ReadAllText(files.SongPath);
            Assert.NotEqual(0, Invoke("instrument", "set", files.SongPath, "--id", "1", "--preset", "unknown").ExitCode);
            Assert.Equal(before, File.ReadAllText(files.SongPath));
        }

        /// <summary>WAV とプリセットの差し替えは排他を保ち、undo/redo で名前参照を復元する。</summary>
        [Fact]
        public void ImportAndPresetReplacement_PreserveExclusivityAndHistory()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { 0, 10000, 0, -10000 });
            RequireSuccess("new", files.SongPath, "--chip", "snes");
            RequireSuccess("instrument", "set", files.SongPath, "--id", "1", "--preset", "strings");
            RequireSuccess("instrument", "import-wav", files.SongPath, files.WavePath, "--id", "1");
            Assert.Null(GetInstrument(files.SongPath, 1).Preset);
            Assert.NotNull(GetInstrument(files.SongPath, 1).SampleData);
            RequireSuccess("undo", files.SongPath);
            Assert.Equal("strings", GetInstrument(files.SongPath, 1).Preset);
            RequireSuccess("redo", files.SongPath);
            Assert.NotNull(GetInstrument(files.SongPath, 1).SampleData);
            RequireSuccess("instrument", "set", files.SongPath, "--id", "1", "--preset", "snare");
            Assert.Null(GetInstrument(files.SongPath, 1).SampleData);
            Assert.False(GetInstrument(files.SongPath, 1).Loop);
        }

        /// <summary>同梱バッチから四小節・八トラックを CLI で再生成し、WAV が非無音・無クリップであることを確認する。</summary>
        [Fact]
        public void Demo_ReproducesCheckedInSongAndExportsAudibleUnclippedWav()
        {
            const int FourBarsTicks = 768;
            const int MinimumNotes = 2;
            const int MaximumNotes = 4;
            using SampleFileFixture files = new SampleFileFixture();
            string examples = Path.Combine(AppContext.BaseDirectory, "Examples");
            RequireSuccess("new", files.SongPath, "--chip", "snes", "--bank", "orchestral", "--length-beats", "16", "--title", "snes-demo");
            RequireSuccess("apply", files.SongPath, "--operations", Path.Combine(examples, "snes-demo-ops.json"));
            Song generated = SongSerializer.Load(files.SongPath);
            Song expected = SongSerializer.Load(Path.Combine(examples, "snes-demo.arpeggio.json"));
            Assert.Equal(SongSerializer.Serialize(expected), SongSerializer.Serialize(generated));
            Assert.Equal(FourBarsTicks, generated.LengthTicks);
            Assert.Equal(8, generated.Tracks.Count);
            Assert.All(generated.Tracks, track => Assert.InRange(track.Notes.Count, MinimumNotes, MaximumNotes));
            Assert.All(generated.Instruments, instrument => Assert.Null(Assert.IsType<SnesSampleInstrument>(instrument).SampleData));
            RequireSuccess("export", "wav", files.SongPath, files.WavePath, "--tail", "0", "--sample-rate", "32000");
            float[] samples = WavReader.Read(files.WavePath, out int sampleRate);
            AnalysisReport report = AudioAnalyzer.Analyze(samples, sampleRate, new AnalysisSettings());
            Assert.True(report.RmsDbfs > -60);
            Assert.Equal(0L, report.ClippedSampleCount);
            Assert.True(report.PeakDbfs < 0);
        }

        private static SnesSampleInstrument GetInstrument(string path, int instrumentId)
            => Assert.IsType<SnesSampleInstrument>(SongSerializer.Load(path).Instruments.Single(instrument => instrument.Id == instrumentId));

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
                return (CliExecution.Run(arguments), output.ToString(), error.ToString());
            }
            finally
            {
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }
        }
    }
}
