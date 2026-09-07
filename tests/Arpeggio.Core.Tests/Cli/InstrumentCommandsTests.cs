using System;
using System.IO;
using System.Linq;
using Arpeggio.Cli;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>CLI の音色操作をプロセスを起動せず、保存結果から検証する。</summary>
    [Collection("Cli")]
    public sealed class InstrumentCommandsTests
    {
        /// <summary>全 kind の既定音色を追加して削除できる。</summary>
        [Theory]
        [InlineData("nes", "NesPulse")]
        [InlineData("nes", "NesTriangle")]
        [InlineData("nes", "NesNoise")]
        [InlineData("nes", "NesDpcm")]
        [InlineData("gameboy", "GbPulse")]
        [InlineData("gameboy", "GbWave")]
        [InlineData("gameboy", "GbNoise")]
        [InlineData("snes", "SnesSample")]
        public void AddsAndRemovesEveryKind(string chip, string kind)
        {
            WithSong(chip, path =>
            {
                Assert.Equal(0, Run("instrument", "add", path, "--kind", kind, "--name", "voice"));
                Song song = SongSerializer.Load(path);
                Instrument added = song.Instruments.Single(instrument => instrument.Id == 2);
                Assert.Equal(kind, added.Kind.ToString());
                Assert.Equal("voice", added.Name);
                Assert.Equal(0, Run("instrument", "list", path));
                Assert.Equal(0, Run("instrument", "list", path, "--json"));
                Assert.Equal(0, Run("instrument", "remove", path, "--id", "2"));
                Assert.Single(SongSerializer.Load(path).Instruments);
            });
        }

        /// <summary>部分更新で既存のデューティとマクロを保持し、null で解除する。</summary>
        [Fact]
        public void SetsOnlySpecifiedPropertiesAndClearsMacro()
        {
            WithSong("nes", path =>
            {
                Assert.Equal(0, Run("instrument", "add", path, "--kind", "NesPulse", "--name", "lead", "--duty", "12.5",
                    "--volume-macro", "15,14,12/2", "--arpeggio-macro", "0,4,7/0", "--pitch-macro", "-10,0,10", "--duty-macro", "1,2,3,4"));
                Assert.Equal(0, Run("instrument", "set", path, "--id", "2", "--name", "updated", "--pitch-macro", "null"));
                NesPulseInstrument instrument = Assert.IsType<NesPulseInstrument>(SongSerializer.Load(path).Instruments[1]);
                Assert.Equal("updated", instrument.Name);
                Assert.Equal(DutyCycle.Percent12_5, instrument.Duty);
                Assert.Equal(new[] { 15, 14, 12 }, instrument.VolumeMacro!.Values);
                Assert.Equal(2, instrument.VolumeMacro.LoopIndex);
                Assert.Equal(new[] { 0, 4, 7 }, instrument.ArpeggioMacro!.Values);
                Assert.Equal(new[] { 1, 2, 3, 4 }, instrument.DutyMacro!.Values);
                Assert.Null(instrument.PitchMacro);
            });
        }

        /// <summary>GB のエンベロープ・波形・ノイズ幅を保存する。</summary>
        [Fact]
        public void AppliesGameBoyParameters()
        {
            WithSong("gameboy", path =>
            {
                Assert.Equal(0, Run("instrument", "set", path, "--id", "1", "--duty", "25", "--initial-volume", "9",
                    "--envelope-increasing", "true", "--envelope-step-frames", "3"));
                GbPulseInstrument pulse = Assert.IsType<GbPulseInstrument>(SongSerializer.Load(path).Instruments[0]);
                Assert.Equal(DutyCycle.Percent25, pulse.Duty);
                Assert.Equal(9, pulse.InitialVolume);
                Assert.True(pulse.EnvelopeIncreasing);
                Assert.Equal(3, pulse.EnvelopeStepFrames);
                const string Waveform = "0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15";
                Assert.Equal(0, Run("instrument", "add", path, "--kind", "GbWave", "--name", "wave", "--waveform", Waveform, "--output-level", "25"));
                GbWaveInstrument wave = Assert.IsType<GbWaveInstrument>(SongSerializer.Load(path).Instruments[1]);
                Assert.Equal(32, wave.Waveform.Length);
                Assert.Equal(15, wave.Waveform[31]);
                Assert.Equal(25, wave.OutputLevel);
                Assert.Equal(0, Run("instrument", "add", path, "--kind", "GbNoise", "--name", "noise", "--lfsr-width", "7"));
                Assert.Equal(7, Assert.IsType<GbNoiseInstrument>(SongSerializer.Load(path).Instruments[2]).LfsrWidth);
            });
        }

        /// <summary>SNES の波形・ADSR・ループ・パン・エコー送りを保存する。</summary>
        [Fact]
        public void AppliesSnesParameters()
        {
            WithSong("snes", path =>
            {
                Assert.Equal(0, Run("instrument", "set", path, "--id", "1", "--waveform", "Saw", "--loop", "false",
                    "--adsr", "0.01,0.2,0.6,0.3", "--pan", "-0.5", "--echo-send", "0.4"));
                SnesSampleInstrument sample = Assert.IsType<SnesSampleInstrument>(SongSerializer.Load(path).Instruments[0]);
                Assert.Equal(SnesWaveformKind.Saw, sample.Waveform);
                Assert.False(sample.Loop);
                Assert.Equal(new AdsrEnvelope(0.01, 0.2, 0.6, 0.3), sample.Envelope);
                Assert.Equal(-0.5, sample.Pan);
                Assert.Equal(0.4, sample.EchoSend);
            });
        }

        /// <summary>不適合オプション・不正構文を拒否し、ファイルを変更しない。</summary>
        [Theory]
        [InlineData("--waveform", "Sine")]
        [InlineData("--duty", "10")]
        [InlineData("--volume-macro", "15/1")]
        [InlineData("--kind", "None")]
        [InlineData("--loop", "true")]
        public void RejectsInvalidOptionsWithoutSaving(string option, string value)
        {
            WithSong("nes", path =>
            {
                string before = File.ReadAllText(path);
                Assert.Equal(1, Run("instrument", "set", path, "--id", "1", option, value));
                Assert.Equal(before, File.ReadAllText(path));
            });
        }

        /// <summary>kind 変更では共有マクロを保ち、新 kind 専用パラメータを適用する。</summary>
        [Fact]
        public void ChangesKindWithSharedParameters()
        {
            WithSong("nes", path =>
            {
                Assert.Equal(0, Run("instrument", "set", path, "--id", "1", "--name", "changed", "--volume-macro", "15,12"));
                Assert.Equal(0, Run("instrument", "set", path, "--id", "1", "--kind", "NesNoise", "--noise-mode", "Short"));
                NesNoiseInstrument noise = Assert.IsType<NesNoiseInstrument>(SongSerializer.Load(path).Instruments[0]);
                Assert.Equal("changed", noise.Name);
                Assert.Equal(NoiseMode.Short, noise.NoiseMode);
                Assert.Equal(new[] { 15, 12 }, noise.VolumeMacro!.Values);
            });
        }

        private static int Run(params string[] arguments)
        {
            return CliExecution.Run(arguments);
        }

        private static void WithSong(string chip, Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-instrument-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "song.arpeggio.json");
                Assert.Equal(0, Run("new", path, "--chip", chip));
                action(path);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
