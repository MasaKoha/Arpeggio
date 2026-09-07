using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Nes
{
    /// <summary>マクロとノート効果が 60 Hz の発音状態へ接続されることを検証する。</summary>
    public sealed class NesModulationTests
    {
        private const int FrameRate = 60;

        /// <summary>音量・アルペジオ・ピッチ・デューティのマクロを同一フレームに適用する。</summary>
        [Fact]
        public void AdvanceFrame_AppliesAllInstrumentMacros()
        {
            var instrument = new NesPulseInstrument
            {
                VolumeMacro = new Macro { Values = new int[] { 15, 7 }, LoopIndex = -1 },
                ArpeggioMacro = new Macro { Values = new int[] { 0, 12 }, LoopIndex = -1 },
                PitchMacro = new Macro { Values = new int[] { 0, 100 }, LoopIndex = -1 },
                DutyMacro = new Macro { Values = new int[] { (int)DutyCycle.Percent50, (int)DutyCycle.Percent25 }, LoopIndex = -1 }
            };
            var synthesizer = new NesPulseSynthesizer();
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            var initial = new float[SynthSamples.SampleRate];
            synthesizer.Render(initial);
            synthesizer.AdvanceFrame();
            var advanced = new float[SynthSamples.SampleRate];
            synthesizer.Render(advanced);
            double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote + 13);
            double actualFrequency = SignalAnalysis.EstimateFrequency(advanced, SynthSamples.SampleRate);

            Assert.InRange(Math.Abs(actualFrequency / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.HighRatio(advanced) - 0.25), 0, SignalAnalysis.DutyTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.RootMeanSquare(advanced) / SignalAnalysis.RootMeanSquare(initial) - 7.0 / 15), 0, SignalAnalysis.SilenceTolerance);
        }

        /// <summary>PitchSlide と VolumeSlide はノート期間の半分で中間値になる。</summary>
        [Fact]
        public void AdvanceFrame_SlidesReachMidpointOfNoteDuration()
        {
            const int PitchSlideSemitones = 12;
            const int VolumeSlide = -15;
            var synthesizer = new NesPulseSynthesizer();
            var effects = new NoteEffect[]
            {
                new NoteEffect(NoteEffectKind.PitchSlide, PitchSlideSemitones),
                new NoteEffect(NoteEffectKind.VolumeSlide, VolumeSlide)
            };
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, new NesPulseInstrument(), effects);
            synthesizer.SetNoteDuration(1);
            for (int frame = 0; frame < FrameRate / 2; frame++)
            {
                synthesizer.AdvanceFrame();
            }

            var samples = new float[SynthSamples.SampleRate];
            synthesizer.Render(samples);
            double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote + PitchSlideSemitones / 2);
            double actualFrequency = SignalAnalysis.EstimateFrequency(samples, SynthSamples.SampleRate);

            Assert.InRange(Math.Abs(actualFrequency / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.RootMeanSquare(samples) - 0.5), 0, SignalAnalysis.SilenceTolerance);
        }

        /// <summary>0xy の半音オフセットが基音・上位桁・下位桁の順で循環する。</summary>
        [Fact]
        public void AdvanceFrame_NoteArpeggioCyclesThroughBothNibbles()
        {
            const int ArpeggioValue = 0x47;
            var synthesizer = new NesPulseSynthesizer();
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, new NesPulseInstrument(),
                new NoteEffect[] { new NoteEffect(NoteEffectKind.Arpeggio, ArpeggioValue) });
            int[] offsets = new int[] { 0, 4, 7, 0 };
            var samples = new float[SynthSamples.SampleRate];
            foreach (int offset in offsets)
            {
                Array.Clear(samples, 0, samples.Length);
                synthesizer.Render(samples);
                double actualFrequency = SignalAnalysis.EstimateFrequency(samples, SynthSamples.SampleRate);
                double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote + offset);
                Assert.InRange(Math.Abs(actualFrequency / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
                synthesizer.AdvanceFrame();
            }
        }
    }
}
