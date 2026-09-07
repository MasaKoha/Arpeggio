using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Nes
{
    /// <summary>固定音量と 4 bit 階段三角波を検証する。</summary>
    public sealed class NesTriangleSynthesizerTests
    {
        /// <summary>三角波の音程を数値検証し、音量指定で振幅が変わらない。</summary>
        [Fact]
        public void Render_MatchesFrequencyAndIgnoresVolume()
        {
            var instrument = new NesTriangleInstrument();
            float[] loud = SynthSamples.Render(new NesTriangleSynthesizer(), instrument);
            float[] quiet = SynthSamples.Render(new NesTriangleSynthesizer(), instrument, SynthSamples.QuietVolume);
            double frequency = SignalAnalysis.EstimateFrequency(loud, SynthSamples.SampleRate);
            double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote);

            Assert.InRange(Math.Abs(frequency / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
            Assert.Equal(loud, quiet);
            Assert.True(SignalAnalysis.RootMeanSquare(loud) > SignalAnalysis.SilenceTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.HighRatio(loud) - 0.5), 0, SignalAnalysis.DutyTolerance);
        }

        /// <summary>32 ステップ周期が 16 種類の振幅を使う。</summary>
        [Fact]
        public void Render_UsesFourBitAmplitudeLevels()
        {
            const int AmplitudeLevelCount = 16;
            float[] samples = SynthSamples.Render(new NesTriangleSynthesizer(), new NesTriangleInstrument());
            var levels = new HashSet<float>(samples);

            Assert.Equal(AmplitudeLevelCount, levels.Count);
        }
    }
}
