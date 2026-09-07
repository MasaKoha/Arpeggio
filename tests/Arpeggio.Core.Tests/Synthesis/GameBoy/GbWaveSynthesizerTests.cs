using System;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.GameBoy;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.GameBoy
{
    /// <summary>波形メモリの周波数、High 比率と出力レベルを検証する。</summary>
    public sealed class GbWaveSynthesizerTests
    {
        /// <summary>32 サンプル波形の 25 % デューティと基準音程を保つ。</summary>
        [Fact]
        public void Render_UsesWaveMemoryAtRequestedFrequency()
        {
            float[] samples = SynthSamples.Render(new GbWaveSynthesizer(), CreatePulseWave());
            double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote);
            double frequency = SignalAnalysis.EstimateFrequency(samples, SynthSamples.SampleRate);

            Assert.InRange(Math.Abs(frequency / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.HighRatio(samples) - 0.25), 0, SignalAnalysis.DutyTolerance);
        }

        /// <summary>ハードウェア出力段階とノート音量が実効値へ反映される。</summary>
        [Fact]
        public void Render_OutputLevelAndNoteVolumeControlAmplitude()
        {
            var instrument = CreatePulseWave();
            float[] loud = SynthSamples.Render(new GbWaveSynthesizer(), instrument);
            float[] quiet = SynthSamples.Render(new GbWaveSynthesizer(), instrument, SynthSamples.QuietVolume);
            instrument.OutputLevel = 50;
            float[] half = SynthSamples.Render(new GbWaveSynthesizer(), instrument);
            instrument.OutputLevel = 25;
            float[] quarter = SynthSamples.Render(new GbWaveSynthesizer(), instrument);
            instrument.OutputLevel = 0;
            float[] silent = SynthSamples.Render(new GbWaveSynthesizer(), instrument);

            double loudAmplitude = SignalAnalysis.RootMeanSquare(loud);
            Assert.True(loudAmplitude > SignalAnalysis.RootMeanSquare(quiet) + SignalAnalysis.SilenceTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.RootMeanSquare(half) / loudAmplitude - 0.5), 0, SignalAnalysis.SilenceTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.RootMeanSquare(quarter) / loudAmplitude - 0.25), 0, SignalAnalysis.SilenceTolerance);
            Assert.InRange(SignalAnalysis.RootMeanSquare(silent), 0, SignalAnalysis.SilenceTolerance);
        }

        private static GbWaveInstrument CreatePulseWave()
        {
            const int WaveLength = 32;
            const int HighSamples = 8;
            const int MaximumSample = 15;
            var waveform = new int[WaveLength];
            Array.Fill(waveform, MaximumSample, 0, HighSamples);
            return new GbWaveInstrument { Waveform = waveform, OutputLevel = 100 };
        }
    }
}
