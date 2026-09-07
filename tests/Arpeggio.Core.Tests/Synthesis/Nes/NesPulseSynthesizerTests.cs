using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Nes
{
    /// <summary>Nes 矩形波の周波数・デューティ・音量と加算契約を検証する。</summary>
    public sealed class NesPulseSynthesizerTests
    {
        /// <summary>全デューティで基準音程と High 比率を満たす。</summary>
        [Theory]
        [InlineData(DutyCycle.Percent12_5, 0.125)]
        [InlineData(DutyCycle.Percent25, 0.25)]
        [InlineData(DutyCycle.Percent50, 0.5)]
        [InlineData(DutyCycle.Percent75, 0.75)]
        public void Render_MatchesFrequencyAndDuty(DutyCycle duty, double expectedDuty)
        {
            var instrument = new NesPulseInstrument { Duty = duty };
            float[] samples = SynthSamples.Render(new NesPulseSynthesizer(), instrument);
            double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote);
            double frequencyError = Math.Abs(SignalAnalysis.EstimateFrequency(samples, SynthSamples.SampleRate) / expectedFrequency - 1);

            Assert.InRange(frequencyError, 0, SignalAnalysis.FrequencyRelativeTolerance);
            Assert.InRange(Math.Abs(SignalAnalysis.HighRatio(samples) - expectedDuty), 0, SignalAnalysis.DutyTolerance);
        }

        /// <summary>音量を下げると実効値が単調に下がる。</summary>
        [Fact]
        public void Render_VolumeControlsRootMeanSquare()
        {
            var instrument = new NesPulseInstrument();
            float[] loud = SynthSamples.Render(new NesPulseSynthesizer(), instrument);
            float[] quiet = SynthSamples.Render(new NesPulseSynthesizer(), instrument, SynthSamples.QuietVolume);
            float[] silent = SynthSamples.Render(new NesPulseSynthesizer(), instrument, volume: 0);

            Assert.True(SignalAnalysis.RootMeanSquare(loud) > SignalAnalysis.RootMeanSquare(quiet) + SignalAnalysis.SilenceTolerance);
            Assert.True(SignalAnalysis.RootMeanSquare(quiet) > SignalAnalysis.SilenceTolerance);
            Assert.InRange(SignalAnalysis.RootMeanSquare(silent), 0, SignalAnalysis.SilenceTolerance);
        }

        /// <summary>呼び出し側の既存波形を上書きせず、ノートオフ後も保持する。</summary>
        [Fact]
        public void Render_AddsToBufferAndNoteOffStopsAdding()
        {
            const int BufferLength = 256;
            const float ExistingSignal = 0.25f;
            const float ComparisonTolerance = 0.000001f;
            var instrument = new NesPulseInstrument();
            var reference = new NesPulseSynthesizer();
            var synthesizer = new NesPulseSynthesizer();
            reference.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            var expected = new float[BufferLength];
            var actual = new float[BufferLength];
            Array.Fill(actual, ExistingSignal);
            reference.Render(expected);
            synthesizer.Render(actual);
            for (int index = 0; index < actual.Length; index++)
            {
                Assert.InRange(Math.Abs(actual[index] - expected[index] - ExistingSignal), 0, ComparisonTolerance);
            }

            synthesizer.NoteOff();
            Array.Fill(actual, ExistingSignal);
            synthesizer.Render(actual);
            Assert.All(actual, sample => Assert.Equal(ExistingSignal, sample));
        }
    }
}
