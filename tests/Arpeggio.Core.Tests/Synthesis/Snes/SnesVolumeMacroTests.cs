using System;
using System.Runtime.InteropServices;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>SNES の音量マクロと既存 ADSR・サンプル進行の積を検証する。</summary>
    public sealed class SnesVolumeMacroTests
    {
        private const int DspSampleRate = 32000;
        private const int ReferenceNote = 60;
        private const int MaximumNoteVolume = 15;
        private const int MaximumDspVolume = 127;
        private const int MaximumEnvelopeLevel = 2047;
        private const int FastAttackLevel = 1024;
        private const int SamplesPerObservation = 64;
        private const int SourceLength = 16;
        private const double PcmScale = 32768.0;
        private static readonly SnesAdsrRegisters HoldingAdsr = new SnesAdsrRegisters(15, 0, 7, 0);

        /// <summary>全量保持 ADSR の初回二サンプルとノート音量を含め、独立した DSP 期待値に一致する。</summary>
        [Theory]
        [InlineData(15)]
        [InlineData(9)]
        public void VolumeMacroMultipliesHoldingAdsrAndNoteVolume(int noteVolume)
        {
            var instrument = CreateConstantInstrument();
            instrument.VolumeMacro = new Macro { Values = new[] { 12, 8, 4, 0 } };
            BrrSample source = CreateSource(instrument);
            var synthesizer = new SnesVoiceSynthesizer(DspSampleRate);
            synthesizer.NoteOn(ReferenceNote, noteVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            int[] expectedMacro = { 12, 8, 4, 0, 0 };
            for (int frame = 0; frame < expectedMacro.Length; frame++)
            {
                int volume = (int)Math.Round(noteVolume * expectedMacro[frame] / (double)(MaximumNoteVolume * MaximumNoteVolume) * MaximumDspVolume);
                for (int sampleIndex = 0; sampleIndex < SamplesPerObservation; sampleIndex++)
                {
                    int position = frame * SamplesPerObservation + sampleIndex;
                    Assert.Equal(ExpectedDspSample(source, position, volume), synthesizer.ReadDspSample());
                }
                synthesizer.AdvanceFrame();
            }
        }

        /// <summary>null と空列はマクロ追加前の全量保持 DSP 計算と全バイト一致する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingAndEmptyMacrosMatchLegacyDspPcm(bool empty)
        {
            const int NoteVolume = 9;
            var instrument = CreateConstantInstrument();
            instrument.VolumeMacro = empty ? new Macro() : null;
            BrrSample source = CreateSource(instrument);
            var synthesizer = new SnesVoiceSynthesizer(DspSampleRate);
            synthesizer.NoteOn(ReferenceNote, NoteVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            const int ObservationCount = SamplesPerObservation * 4;
            var expected = new short[ObservationCount];
            var actual = new short[ObservationCount];
            int legacyVolume = (int)Math.Round(NoteVolume / (double)MaximumNoteVolume * MaximumDspVolume);
            for (int index = 0; index < ObservationCount; index++)
            {
                expected[index] = ExpectedDspSample(source, index, legacyVolume);
                actual[index] = synthesizer.ReadDspSample();
                if ((index + 1) % SamplesPerObservation == 0)
                {
                    synthesizer.AdvanceFrame();
                }
            }
            Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
        }

        /// <summary>音量ゼロで再生位置を停止せず、ループ復帰時も再打鍵せず同じサンプルを読む。</summary>
        [Fact]
        public void ZeroVolumeContinuesSamplePhaseAndMacroLoop()
        {
            const int ObservationCount = 37;
            var instrument = CreateConstantInstrument();
            instrument.SampleData = SampleDataCodec.Encode(new short[]
            {
                1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000,
                -1000, -2000, -3000, -4000, -5000, -6000, -7000, -8000
            });
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 0, 15 }, LoopIndex = 1 };
            var referenceInstrument = CreateConstantInstrument();
            referenceInstrument.SampleData = instrument.SampleData;
            var reference = new SnesVoiceSynthesizer(DspSampleRate);
            var actual = new SnesVoiceSynthesizer(DspSampleRate);
            reference.NoteOn(ReferenceNote, MaximumNoteVolume, referenceInstrument, ReadOnlySpan<NoteEffect>.Empty);
            actual.NoteOn(ReferenceNote, MaximumNoteVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            int[] expectedVolumes = { 15, 0, 15, 0, 15 };
            foreach (int volume in expectedVolumes)
            {
                for (int index = 0; index < ObservationCount; index++)
                {
                    short expected = reference.ReadDspSample();
                    Assert.Equal(volume == 0 ? (short)0 : expected, actual.ReadDspSample());
                }
                reference.AdvanceFrame();
                actual.AdvanceFrame();
            }
            actual.NoteOn(ReferenceNote, MaximumNoteVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            reference.NoteOn(ReferenceNote, MaximumNoteVolume, referenceInstrument, ReadOnlySpan<NoteEffect>.Empty);
            Assert.Equal(reference.ReadDspSample(), actual.ReadDspSample());
        }

        /// <summary>マクロ未指定の内蔵波形・DSPノイズ・素材・プリセットは空列と全量マクロでも PCM 全バイトを保つ。</summary>
        [Theory]
        [InlineData("Sine")]
        [InlineData("Square")]
        [InlineData("Saw")]
        [InlineData("Triangle")]
        [InlineData("Pulse")]
        [InlineData("Noise")]
        [InlineData("dspNoise")]
        [InlineData("sample")]
        [InlineData("strings")]
        public void EmptyAndFullMacrosPreserveLegacyAudioAndRelease(string source)
        {
            foreach (int sampleRate in new[] { 44100, 48000, 44101 })
            {
                SnesSampleInstrument instrument = CreateInstrument(source);
                byte[] legacy = RenderBytes(instrument, sampleRate);
                instrument.VolumeMacro = new Macro();
                Assert.Equal(legacy, RenderBytes(instrument, sampleRate));
                instrument.VolumeMacro = new Macro { Values = new[] { MaximumNoteVolume } };
                Assert.Equal(legacy, RenderBytes(instrument, sampleRate));
            }
        }

        private static short ExpectedDspSample(BrrSample source, int position, int volume)
        {
            int current = position % SourceLength;
            double previous = position == 0 ? 0 : source.Samples[(current + SourceLength - 1) % SourceLength];
            double sample = GaussianInterpolator.Interpolate(previous, source.Samples[current],
                source.Samples[(current + 1) % SourceLength], source.Samples[(current + 2) % SourceLength], 0);
            double level = position == 0 ? FastAttackLevel / (double)MaximumEnvelopeLevel : 1;
            return (short)Math.Clamp(Math.Round(sample * level * volume / MaximumDspVolume * PcmScale), short.MinValue, short.MaxValue);
        }

        private static BrrSample CreateSource(SnesSampleInstrument instrument)
            => BrrSample.Create(SampleDataCodec.ToFloat(SampleDataCodec.Decode(instrument.SampleData!)));

        private static SnesSampleInstrument CreateConstantInstrument()
        {
            const short Amplitude = 8192;
            var source = new short[SourceLength];
            Array.Fill(source, Amplitude);
            return new SnesSampleInstrument
            {
                SampleRate = DspSampleRate, RootMidiNote = ReferenceNote, SampleData = SampleDataCodec.Encode(source),
                AdsrRegisters = HoldingAdsr, Envelope = new AdsrEnvelope(0, 0, 1, 0), Loop = true
            };
        }

        private static SnesSampleInstrument CreateInstrument(string source)
        {
            var instrument = new SnesSampleInstrument();
            if (Enum.TryParse(source, out SnesWaveformKind waveform))
            {
                instrument.Waveform = waveform;
            }
            else if (source == "dspNoise")
            {
                instrument.NoiseEnabled = true;
            }
            else if (source == "sample")
            {
                instrument = CreateConstantInstrument();
            }
            else
            {
                instrument.ApplyPreset(source);
            }
            instrument.PitchMacro = new Macro { Values = new[] { 0, 50, -100 }, LoopIndex = 1 };
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 4, 7 } };
            return instrument;
        }

        private static byte[] RenderBytes(SnesSampleInstrument instrument, int sampleRate)
        {
            const int FrameCount = 8;
            const int FramesPerSecond = 60;
            const int NoteVolume = 9;
            const int VolumeSlide = -3;
            int frameSamples = sampleRate / FramesPerSecond;
            var synthesizer = new SnesVoiceSynthesizer(sampleRate);
            synthesizer.NoteOn(ReferenceNote, NoteVolume, instrument, new[] { new NoteEffect(NoteEffectKind.VolumeSlide, VolumeSlide) });
            var output = new float[frameSamples * (FrameCount + 1)];
            for (int frame = 0; frame < FrameCount; frame++)
            {
                synthesizer.Render(output.AsSpan(frame * frameSamples, frameSamples));
                synthesizer.AdvanceFrame();
            }
            synthesizer.NoteOff();
            synthesizer.Render(output.AsSpan(FrameCount * frameSamples));
            return MemoryMarshal.AsBytes(output.AsSpan()).ToArray();
        }
    }
}
