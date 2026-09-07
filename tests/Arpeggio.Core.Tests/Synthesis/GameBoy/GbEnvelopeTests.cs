using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.GameBoy;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.GameBoy
{
    /// <summary>ハードウェアエンベロープの方向、間隔と音量上限を検証する。</summary>
    public sealed class GbEnvelopeTests
    {
        /// <summary>減少エンベロープは指定フレーム間隔で一段下がる。</summary>
        [Fact]
        public void AdvanceFrame_DecreasingEnvelopeRespectsStepInterval()
        {
            const int StepFrames = 2;
            var synthesizer = new GbPulseSynthesizer();
            var instrument = new GbPulseInstrument { InitialVolume = 15, EnvelopeIncreasing = false, EnvelopeStepFrames = StepFrames };
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            double initial = ReadAmplitude(synthesizer);
            synthesizer.AdvanceFrame();
            double unchanged = ReadAmplitude(synthesizer);
            synthesizer.AdvanceFrame();
            double decreased = ReadAmplitude(synthesizer);

            Assert.Equal(initial, unchanged);
            Assert.InRange(Math.Abs(decreased / initial - 14.0 / 15), 0, SignalAnalysis.SilenceTolerance);
        }

        /// <summary>増加エンベロープが 15 で飽和する。</summary>
        [Fact]
        public void AdvanceFrame_IncreasingEnvelopeClampsAtMaximum()
        {
            const int FrameCount = 60;
            var synthesizer = new GbPulseSynthesizer();
            var instrument = new GbPulseInstrument { InitialVolume = 7, EnvelopeIncreasing = true, EnvelopeStepFrames = 1 };
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            double initial = ReadAmplitude(synthesizer);
            for (int frame = 0; frame < FrameCount; frame++)
            {
                synthesizer.AdvanceFrame();
            }

            double saturated = ReadAmplitude(synthesizer);
            Assert.True(saturated > initial + SignalAnalysis.SilenceTolerance);
            Assert.InRange(Math.Abs(saturated - 1), 0, SignalAnalysis.SilenceTolerance);
        }

        private static double ReadAmplitude(GbPulseSynthesizer synthesizer)
        {
            const int BufferSamples = 512;
            var samples = new float[BufferSamples];
            synthesizer.Render(samples);
            return SignalAnalysis.RootMeanSquare(samples);
        }
    }
}
