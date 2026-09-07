using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Nes
{
    /// <summary>Nes ノイズの再現性と音量応答を検証する。</summary>
    public sealed class NesNoiseSynthesizerTests
    {
        /// <summary>各 LFSR 構成で再現可能な非定数波形と単調な音量を得る。</summary>
        [Theory]
        [InlineData(NoiseMode.Long)]
        [InlineData(NoiseMode.Short)]
        public void Render_IsDeterministicAndVolumeIsMonotonic(NoiseMode mode)
        {
            var instrument = new NesNoiseInstrument { NoiseMode = mode };
            float[] loud = SynthSamples.Render(new NesNoiseSynthesizer(), instrument);
            float[] repeated = SynthSamples.Render(new NesNoiseSynthesizer(), instrument);
            float[] quiet = SynthSamples.Render(new NesNoiseSynthesizer(), instrument, SynthSamples.QuietVolume);

            Assert.Equal(loud, repeated);
            Assert.Contains(loud, sample => Math.Abs(sample - loud[0]) > SignalAnalysis.SilenceTolerance);
            Assert.True(SignalAnalysis.RootMeanSquare(loud) > SignalAnalysis.RootMeanSquare(quiet) + SignalAnalysis.SilenceTolerance);
            Assert.True(SignalAnalysis.RootMeanSquare(quiet) > SignalAnalysis.SilenceTolerance);
        }

        /// <summary>極端な上行スライドでも整数変換で下限へ反転せず上限周期を選ぶ。</summary>
        [Fact]
        public void AdvanceFrame_ExtremePitchSlideClampsBeforeIntegerConversion()
        {
            const int FramesPerSecond = 60;
            const int MaximumMidiNote = 127;
            const int ObservationSamples = 4096;
            var instrument = new NesNoiseInstrument();
            var sliding = new NesNoiseSynthesizer();
            var expected = new NesNoiseSynthesizer();
            sliding.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument,
                new[] { new NoteEffect(NoteEffectKind.PitchSlide, int.MaxValue) });
            for (int frame = 0; frame < FramesPerSecond; frame++)
            {
                sliding.AdvanceFrame();
            }
            expected.NoteOn(MaximumMidiNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            var actualSamples = new float[ObservationSamples];
            var expectedSamples = new float[ObservationSamples];
            sliding.Render(actualSamples);
            expected.Render(expectedSamples);

            Assert.Equal(expectedSamples, actualSamples);
        }
    }
}
