using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Snes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>内蔵波形の音程、振幅、デューティと ADSR 解放を検証する。</summary>
    public sealed class SnesVoiceSynthesizerTests
    {
        /// <summary>周期波形が基準音程とノート音量に従う。</summary>
        [Theory]
        [InlineData(SnesWaveformKind.Sine)]
        [InlineData(SnesWaveformKind.Square)]
        [InlineData(SnesWaveformKind.Saw)]
        [InlineData(SnesWaveformKind.Triangle)]
        [InlineData(SnesWaveformKind.Pulse)]
        public void Render_MatchesFrequencyAndVolume(SnesWaveformKind waveform)
        {
            var instrument = CreateSustainedInstrument(waveform);
            float[] loud = SynthSamples.Render(new SnesVoiceSynthesizer(), instrument);
            float[] quiet = SynthSamples.Render(new SnesVoiceSynthesizer(), instrument, SynthSamples.QuietVolume);
            double frequency = SignalAnalysis.EstimateFrequency(loud, SynthSamples.SampleRate);
            double expectedFrequency = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote);

            Assert.InRange(Math.Abs(frequency / expectedFrequency - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
            Assert.True(SignalAnalysis.RootMeanSquare(loud) > SignalAnalysis.RootMeanSquare(quiet) + SignalAnalysis.SilenceTolerance);
            Assert.True(SignalAnalysis.RootMeanSquare(quiet) > SignalAnalysis.SilenceTolerance);
        }

        /// <summary>矩形波とパルス波の High 比率を数値検証する。</summary>
        [Theory]
        [InlineData(SnesWaveformKind.Square, 0.5)]
        [InlineData(SnesWaveformKind.Pulse, 0.25)]
        public void Render_MatchesDuty(SnesWaveformKind waveform, double expectedDuty)
        {
            float[] samples = SynthSamples.Render(new SnesVoiceSynthesizer(), CreateSustainedInstrument(waveform));

            Assert.InRange(Math.Abs(SignalAnalysis.HighRatio(samples) - expectedDuty), 0, SignalAnalysis.DutyTolerance);
        }

        /// <summary>ノイズ波形も再現可能で、音量によって実効値が下がる。</summary>
        [Fact]
        public void Render_NoiseIsDeterministicAndAudible()
        {
            var instrument = CreateSustainedInstrument(SnesWaveformKind.Noise);
            float[] loud = SynthSamples.Render(new SnesVoiceSynthesizer(), instrument);
            float[] repeated = SynthSamples.Render(new SnesVoiceSynthesizer(), instrument);
            float[] quiet = SynthSamples.Render(new SnesVoiceSynthesizer(), instrument, SynthSamples.QuietVolume);

            Assert.Equal(loud, repeated);
            Assert.Contains(loud, sample => Math.Abs(sample - loud[0]) > SignalAnalysis.SilenceTolerance);
            Assert.True(SignalAnalysis.RootMeanSquare(loud) > SignalAnalysis.RootMeanSquare(quiet) + SignalAnalysis.SilenceTolerance);
        }

        /// <summary>ノートオフで即断せず、指定した解放時間後に無音へ到達する。</summary>
        [Fact]
        public void NoteOff_ReleasesEnvelopeToSilence()
        {
            const double ReleaseSeconds = 0.05;
            const int ObservationSamples = SynthSamples.SampleRate / 10;
            const int FinalWindowSamples = SynthSamples.SampleRate / 100;
            var instrument = CreateSustainedInstrument(SnesWaveformKind.Sine);
            instrument.Envelope = new AdsrEnvelope(0, 0, 1, ReleaseSeconds);
            var synthesizer = new SnesVoiceSynthesizer();
            synthesizer.NoteOn(SynthSamples.ReferenceNote, SynthSamples.MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            synthesizer.Render(new float[ObservationSamples]);
            synthesizer.NoteOff();
            var release = new float[ObservationSamples];
            synthesizer.Render(release);

            Assert.True(SignalAnalysis.RootMeanSquare(release.AsSpan(0, FinalWindowSamples)) > SignalAnalysis.SilenceTolerance);
            Assert.InRange(SignalAnalysis.RootMeanSquare(release.AsSpan(ObservationSamples - FinalWindowSamples)), 0, SignalAnalysis.SilenceTolerance);
        }

        private static SnesSampleInstrument CreateSustainedInstrument(SnesWaveformKind waveform)
        {
            return new SnesSampleInstrument
            {
                Waveform = waveform,
                Loop = true,
                Envelope = new AdsrEnvelope(0, 0, 1, 0)
            };
        }
    }
}
