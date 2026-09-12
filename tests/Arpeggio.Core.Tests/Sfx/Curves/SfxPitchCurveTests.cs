using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Curves;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Synthesis;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Curves
{
    /// <summary>整数の固定列で音程の二次項・端数・ジャンプ・反復の加算契約を検証する。</summary>
    public sealed class SfxPitchCurveTests
    {
        /// <summary>基準周波数の端数を先頭からセント列へ残し、最寄りanchorと二重加算しない。</summary>
        [Theory]
        [InlineData(220, 57, 0)]
        [InlineData(440, 69, 0)]
        [InlineData(880, 81, 0)]
        [InlineData(450, 69, 39)]
        [InlineData(460, 70, -23)]
        public void Generate_PreservesFractionalBasePitch(double frequency, int anchor, int cents)
        {
            SfxToneCurve curve = Generate(new SfxToneParameters { BaseFrequencyHz = frequency });
            Assert.Equal(anchor, curve.AnchorMidiNote);
            Assert.Equal(new[] { cents, cents, cents, cents }, curve.PitchMacro.Values);
            Assert.Equal(new[] { 0, 0, 0, 0 }, curve.ArpeggioMacro.Values);
        }

        /// <summary>deltaは速度の更新回数ではなく、秒の二次項として積分する。</summary>
        [Theory]
        [InlineData(0, 360, new[] { 0, 5, 20, 45 })]
        [InlineData(0, -360, new[] { 0, -5, -20, -45 })]
        [InlineData(-12, 360, new[] { 0, -15, -20, -15 })]
        [InlineData(12, -360, new[] { 0, 15, 20, 15 })]
        [InlineData(1.5, 0, new[] { 0, 3, 5, 8 })]
        [InlineData(-1.5, 0, new[] { 0, -3, -5, -8 })]
        public void Generate_IntegratesDeltaAndRoundsSignedMidpoints(double slide, double delta, int[] expected)
        {
            SfxToneCurve curve = Generate(new SfxToneParameters
            {
                SlideSemitonesPerSecond = slide, DeltaSlideSemitonesPerSecondSquared = delta
            });
            Assert.Equal(expected, curve.PitchMacro.Values);
        }

        /// <summary>6 Hz以外も受理し、深さ0または速度0は全域で揺れなしにする。</summary>
        [Theory]
        [InlineData(100, 15, new[] { 0, 100, 0, -100 })]
        [InlineData(100, 10, new[] { 0, 87, 87, 0 })]
        [InlineData(200, 20, new[] { 0, 173, -173, 0 })]
        [InlineData(0, 15, new[] { 0, 0, 0, 0 })]
        [InlineData(100, 0, new[] { 0, 0, 0, 0 })]
        public void Generate_UsesRequestedVibratoSpeed(double depth, double speed, int[] expected)
        {
            SfxToneCurve curve = Generate(new SfxToneParameters { VibratoDepthCents = depth, VibratoSpeedHz = speed });
            Assert.Equal(expected, curve.PitchMacro.Values);
        }

        /// <summary>ジャンプは待ちフレームから一度だけ半音列へ入り、ピッチ列へ混ぜない。</summary>
        [Theory]
        [InlineData(7, 0, new[] { 7, 7, 7, 7 })]
        [InlineData(-7, 1.0 / 60, new[] { 0, -7, -7, -7 })]
        [InlineData(7, 2.0 / 60, new[] { 0, 0, 7, 7 })]
        [InlineData(7, 0.05, new[] { 0, 0, 0, 7 })]
        [InlineData(7, 5, new[] { 0, 0, 0, 0 })]
        [InlineData(0, 0, new[] { 0, 0, 0, 0 })]
        public void Generate_SeparatesPitchChangeFromPitchMacro(int semitones, double seconds, int[] expected)
        {
            SfxToneCurve curve = Generate(new SfxToneParameters
            {
                PitchChangeSemitones = semitones, PitchChangeTimeSeconds = seconds,
                SlideSemitonesPerSecond = -12
            });
            Assert.Equal(expected, curve.ArpeggioMacro.Values);
            Assert.Equal(new[] { 0, -20, -40, -60 }, curve.PitchMacro.Values);
        }

        /// <summary>反復周期でslideとdeltaを戻し、包絡を戻さない。</summary>
        [Theory]
        [InlineData(0, new[] { 0, -20, -40, -60 })]
        [InlineData(1.0 / 60, new[] { 0, 0, 0, 0 })]
        [InlineData(2.0 / 60, new[] { 0, -20, 0, -20 })]
        [InlineData(0.05, new[] { 0, -20, -40, 0 })]
        [InlineData(5, new[] { 0, -20, -40, -60 })]
        public void Generate_RepeatsOnlyCurveTime(double period, int[] expected)
        {
            SfxToneCurve curve = Generate(new SfxToneParameters
            {
                SlideSemitonesPerSecond = -12, RepeatPeriodSeconds = period
            });
            Assert.Equal(expected, curve.PitchMacro.Values);
            Assert.Equal(new[] { 12, 8, 4, 0 }, curve.Envelope.VolumeMacro.Values);
        }

        /// <summary>反復で二次項・ジャンプを戻してもビブラートの位相は進み続ける。</summary>
        [Fact]
        public void Generate_RepeatsDeltaAndJumpWhileContinuingVibrato()
        {
            SfxToneCurve curve = Generate(new SfxToneParameters
            {
                SlideSemitonesPerSecond = -12,
                DeltaSlideSemitonesPerSecondSquared = 360,
                VibratoDepthCents = 100,
                VibratoSpeedHz = 15,
                PitchChangeSemitones = 7,
                PitchChangeTimeSeconds = 1.0 / 60,
                RepeatPeriodSeconds = 2.0 / 60
            });
            Assert.Equal(new[] { 0, 85, 0, -115 }, curve.PitchMacro.Values);
            Assert.Equal(new[] { 0, 7, 0, 7 }, curve.ArpeggioMacro.Values);
            Assert.Equal(new[] { 12, 8, 4, 0 }, curve.Envelope.VolumeMacro.Values);
            Assert.Equal(2, curve.RepeatFrames);
            Assert.Equal(1, curve.PitchChangeFrames);
        }

        /// <summary>既存VoiceModulationで先頭値を一度だけ適用し、最終値を保持できる。</summary>
        [Fact]
        public void Generate_ConnectsToExistingModulationWithoutSkippingOrDoubleAddition()
        {
            SfxToneCurve curve = Generate(new SfxToneParameters
            {
                BaseFrequencyHz = 450, SlideSemitonesPerSecond = -12,
                PitchChangeSemitones = 7, PitchChangeTimeSeconds = 1.0 / 60
            });
            VoiceModulation modulation = new VoiceModulation();
            modulation.Start(curve.AnchorMidiNote, 15, Array.Empty<NoteEffect>());
            modulation.Configure(curve.Envelope.VolumeMacro, curve.ArpeggioMacro, curve.PitchMacro);
            double[] expectedPitch = { 69.39, 76.19, 75.99, 75.79, 75.79 };
            double[] expectedVolume = { 12.0 / 15, 8.0 / 15, 4.0 / 15, 0, 0 };
            for (int frame = 0; frame < expectedPitch.Length; frame++)
            {
                Assert.Equal(expectedPitch[frame], modulation.MidiNote, 10);
                Assert.Equal(expectedVolume[frame], modulation.Volume, 10);
                modulation.AdvanceFrame();
            }
        }

        /// <summary>再生成結果の可変マクロ配列を共有せず、要求入力も変更しない。</summary>
        [Fact]
        public void Generate_IsDeterministicAndOwnsEveryMacro()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters
                {
                    SlideSemitonesPerSecond = -12.1234567, DeltaSlideSemitonesPerSecondSquared = 360,
                    VibratoDepthCents = 100, RepeatPeriodSeconds = 2.0 / 60
                },
                Noise = new SfxNoiseParameters { Enabled = true }
            };
            SfxCurveGenerationResult first = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            SfxCurveGenerationResult second = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            SfxToneCurve firstTone = Assert.IsType<SfxToneCurve>(first.Tone);
            SfxToneCurve secondTone = Assert.IsType<SfxToneCurve>(second.Tone);
            Assert.Equal(first.Parameters, second.Parameters);
            Assert.Equal(first.Warnings, second.Warnings);
            Assert.Equal(firstTone.PitchMacro.Values, secondTone.PitchMacro.Values);
            Assert.Equal(firstTone.ArpeggioMacro.Values, secondTone.ArpeggioMacro.Values);
            Assert.Equal(firstTone.Envelope.VolumeMacro.Values, secondTone.Envelope.VolumeMacro.Values);
            Assert.NotSame(firstTone.PitchMacro.Values, secondTone.PitchMacro.Values);
            Assert.NotSame(firstTone.Envelope.VolumeMacro.Values, Assert.IsType<SfxEnvelopeCurve>(first.Noise).VolumeMacro.Values);
            firstTone.PitchMacro.Values[0] = int.MaxValue;
            firstTone.ArpeggioMacro.Values[0] = int.MaxValue;
            firstTone.Envelope.VolumeMacro.Values[0] = int.MaxValue;
            Assert.IsType<SfxEnvelopeCurve>(first.Noise).VolumeMacro.Values[0] = int.MaxValue;
            Assert.Equal(0, secondTone.PitchMacro.Values[0]);
            Assert.Equal(0, secondTone.ArpeggioMacro.Values[0]);
            Assert.Equal(12, secondTone.Envelope.VolumeMacro.Values[0]);
            Assert.Equal(12, Assert.IsType<SfxEnvelopeCurve>(second.Noise).VolumeMacro.Values[0]);
            Assert.Equal(-12.1234567, parameters.Tone.SlideSemitonesPerSecond);
            Assert.Equal(-12.123457, first.Parameters.Tone.SlideSemitonesPerSecond);
            Assert.Equal(2.0 / 60, parameters.Tone.RepeatPeriodSeconds);
            Assert.Equal(0.033333, first.Parameters.Tone.RepeatPeriodSeconds);
        }

        private static SfxToneCurve Generate(SfxToneParameters tone)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = tone with { Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 } }
            };
            return Assert.IsType<SfxToneCurve>(SfxCurveGenerator.Generate(parameters, ChipKind.Nes).Tone);
        }
    }
}
