using Arpeggio.Core.Document;
using Arpeggio.Core.Sequencing;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Synthesis;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>手計算した包絡列と、要求秒数・量子化時間・終端保持の境界。</summary>
    public sealed class SfxEnvelopeCurveTests
    {
        /// <summary>設計の3フレーム減衰を、三チップで同じ8 tickの列へ変換する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Generate_ProducesHandCalculatedDecay(ChipKind chip)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(chip) with
            {
                Tone = new SfxToneParameters
                {
                    SlideSemitonesPerSecond = -12,
                    Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 }
                }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, chip);
            SfxToneCurve tone = Assert.IsType<SfxToneCurve>(result.Tone);
            Assert.Equal(new[] { 12, 8, 4, 0 }, tone.Envelope.VolumeMacro.Values);
            Assert.Equal(new[] { 0, -20, -40, -60 }, tone.PitchMacro.Values);
            Assert.Equal(new[] { 0, 0, 0, 0 }, tone.ArpeggioMacro.Values);
            Assert.Equal(69, tone.AnchorMidiNote);
            Assert.Equal(3, tone.Envelope.EnvelopeFrames);
            Assert.Equal(4, result.BodyFrames);
            Assert.Equal(8, tone.Envelope.DurationTicks);
            Assert.Equal(8, result.LengthTicks);
            Assert.Equal(0.05, tone.Envelope.RequestedEnvelopeSeconds);
            Assert.Equal(0.05, tone.Envelope.EnvelopeDurationSeconds);
            Assert.Equal(4.0 / 60, tone.Envelope.BodyDurationSeconds);
            Assert.Equal(4.0 / 60, result.BodyDurationSeconds);
            Assert.Equal(1, result.GeneratorVersion);
            Assert.Equal(-1, tone.Envelope.VolumeMacro.LoopIndex);
            Assert.Equal(-1, tone.PitchMacro.LoopIndex);
            Assert.Equal(-1, tone.ArpeggioMacro.LoopIndex);
            Assert.Null(result.Noise);
            Assert.Empty(result.Warnings);
        }

        /// <summary>attack/sustainの省略とpunchの区間境界で、ゼロ除算やピーク超過を起こさない。</summary>
        [Theory]
        [InlineData(0, 0, 3, 0, new[] { 12, 8, 4, 0 })]
        [InlineData(2, 0, 2, 0, new[] { 0, 6, 12, 6, 0 })]
        [InlineData(0, 2, 2, 0, new[] { 12, 12, 12, 6, 0 })]
        [InlineData(2, 2, 2, 0, new[] { 0, 6, 12, 12, 12, 6, 0 })]
        [InlineData(2, 2, 2, 1, new[] { 0, 2, 12, 8, 4, 2, 0 })]
        [InlineData(0, 2, 2, 0.25, new[] { 12, 10, 8, 4, 0 })]
        [InlineData(0, 1, 1, 1, new[] { 12, 4, 0 })]
        [InlineData(0, 0, 1, 0, new[] { 12, 0 })]
        public void Generate_RespectsEnvelopeSegments(int attackFrames, int sustainFrames, int decayFrames,
            double punch, int[] expected)
        {
            SfxEnvelopeParameters envelope = new SfxEnvelopeParameters
            {
                AttackSeconds = attackFrames / 60.0,
                SustainSeconds = sustainFrames / 60.0,
                DecaySeconds = decayFrames / 60.0,
                Punch = punch
            };
            SfxEnvelopeCurve curve = GenerateToneEnvelope(envelope);
            Assert.Equal(expected, curve.VolumeMacro.Values);
            Assert.Equal(attackFrames, curve.AttackFrames);
            Assert.Equal(sustainFrames, curve.SustainFrames);
            Assert.Equal(decayFrames, curve.DecayFrames);
            Assert.All(curve.VolumeMacro.Values, value => Assert.InRange(value, 0, 12));
        }

        /// <summary>音量の中間値は偶数丸めせずAwayFromZeroで整数化する。</summary>
        [Fact]
        public void Generate_RoundsVolumeMidpointAwayFromZero()
        {
            SfxEnvelopeCurve curve = GenerateToneEnvelope(new SfxEnvelopeParameters
            {
                Volume = 15, SustainSeconds = 0, DecaySeconds = 2.0 / 60
            });
            Assert.Equal(new[] { 15, 8, 0 }, curve.VolumeMacro.Values);
        }

        /// <summary>半フレーム境界は6桁の正規化後に判定する。</summary>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(0.008333, 0)]
        [InlineData(1.0 / 120, 0)]
        [InlineData(0.0083335, 1)]
        [InlineData(0.008334, 1)]
        [InlineData(0.024999, 1)]
        [InlineData(0.025, 2)]
        [InlineData(0.025001, 2)]
        public void Generate_QuantizesEachSegmentAfterNormalization(double seconds, int expectedFrames)
        {
            SfxEnvelopeCurve curve = GenerateToneEnvelope(new SfxEnvelopeParameters
            {
                AttackSeconds = seconds, SustainSeconds = seconds, DecaySeconds = 0.05
            });
            Assert.Equal(expectedFrames, curve.AttackFrames);
            Assert.Equal(expectedFrames, curve.SustainFrames);
            Assert.Equal(3, curve.DecayFrames);
            Assert.Equal(expectedFrames * 2 + 4, curve.VolumeMacro.Values.Length);
        }

        /// <summary>異なる二包絡の値・音長は独立し、無効レイヤーの長さを除外する。</summary>
        [Fact]
        public void Generate_KeepsLayerEnvelopesIndependent()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters
                {
                    Envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 }
                },
                Noise = new SfxNoiseParameters
                {
                    Enabled = true,
                    Envelope = new SfxEnvelopeParameters { Volume = 6, SustainSeconds = 0, DecaySeconds = 0.1 }
                }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            Assert.Equal(new[] { 12, 8, 4, 0 }, Assert.IsType<SfxToneCurve>(result.Tone).Envelope.VolumeMacro.Values);
            SfxEnvelopeCurve noise = Assert.IsType<SfxEnvelopeCurve>(result.Noise);
            Assert.Equal(new[] { 6, 5, 4, 3, 2, 1, 0 }, noise.VolumeMacro.Values);
            Assert.Equal(14, result.LengthTicks);
            Assert.Equal(0.1, noise.RequestedEnvelopeSeconds);
            Assert.Equal(6, noise.EnvelopeFrames);
            SfxCurveGenerationResult toneOnly = SfxCurveGenerator.Generate(
                parameters with { Noise = parameters.Noise with { Enabled = false } }, ChipKind.Nes);
            Assert.Null(toneOnly.Noise);
            Assert.Equal(8, toneOnly.LengthTicks);
            SfxCurveGenerationResult noiseOnly = SfxCurveGenerator.Generate(
                parameters with { Tone = parameters.Tone with { Enabled = false } }, ChipKind.Nes);
            Assert.Null(noiseOnly.Tone);
            Assert.Equal(14, noiseOnly.LengthTicks);
        }

        /// <summary>最大入力でも各列は301要素、本体602 tickに収まり、0音量の有効レイヤーも長さを保つ。</summary>
        [Fact]
        public void Generate_BoundsResourcesAtMaximumEnvelope()
        {
            SfxEnvelopeParameters envelope = new SfxEnvelopeParameters
            {
                AttackSeconds = 1, SustainSeconds = 2, DecaySeconds = 2, Punch = 1, Volume = 0
            };
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters
                {
                    Envelope = envelope, SlideSemitonesPerSecond = 360,
                    DeltaSlideSemitonesPerSecondSquared = 1440, VibratoDepthCents = 200, VibratoSpeedHz = 20
                },
                Noise = new SfxNoiseParameters { Enabled = true, Envelope = envelope }
            };
            SfxCurveGenerationResult result = SfxCurveGenerator.Generate(parameters, ChipKind.Nes);
            SfxToneCurve tone = Assert.IsType<SfxToneCurve>(result.Tone);
            SfxEnvelopeCurve noise = Assert.IsType<SfxEnvelopeCurve>(result.Noise);
            Assert.Equal(300, tone.Envelope.EnvelopeFrames);
            Assert.Equal(301, tone.Envelope.VolumeMacro.Values.Length);
            Assert.Equal(301, tone.PitchMacro.Values.Length);
            Assert.Equal(301, tone.ArpeggioMacro.Values.Length);
            Assert.Equal(301, noise.VolumeMacro.Values.Length);
            Assert.Equal(602, result.LengthTicks);
            Assert.Equal(301.0 / 60, result.BodyDurationSeconds);
            Assert.Equal(1980000, tone.PitchMacro.Values[300]);
            Assert.All(tone.Envelope.VolumeMacro.Values, value => Assert.Equal(0, value));
            Assert.All(noise.VolumeMacro.Values, value => Assert.Equal(0, value));
        }

        /// <summary>不正・過大・非有限の時間はマクロ確保より前に入力エラーとして拒否する。</summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(2.000001)]
        [InlineData(double.MaxValue)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Generate_RejectsInvalidDuration(double seconds)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Noise = new SfxNoiseParameters { Envelope = new SfxEnvelopeParameters { DecaySeconds = seconds } }
            };
            SfxParameterException exception = Assert.Throws<SfxParameterException>(
                () => SfxCurveGenerator.Generate(parameters, ChipKind.Nes));
            Assert.Equal("InvalidParameter", exception.Code);
            Assert.Equal("noise.envelope.decaySeconds", exception.ParameterPath);
        }

        /// <summary>割り切れないサンプルレートでも既存時計で終端ゼロが本体末尾より先に適用される。</summary>
        [Theory]
        [InlineData(44100, 2205L, 2940L)]
        [InlineData(48000, 2400L, 3200L)]
        [InlineData(44101, 2206L, 2940L)]
        public void Generate_HoldsTerminalZeroAcrossClockBoundaries(int sampleRate, long zeroSample, long endSample)
        {
            SfxEnvelopeCurve curve = GenerateToneEnvelope(new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.05 });
            FrameClock frameClock = new FrameClock(sampleRate);
            TickClock tickClock = new TickClock(150, sampleRate);
            Assert.Equal(endSample, tickClock.TickToSamples(curve.DurationTicks));
            Assert.Equal(2L, frameClock.GetFrame(zeroSample - 1));
            Assert.Equal(3L, frameClock.GetFrame(zeroSample));
            Assert.Equal(4, MacroRunner.GetValue(curve.VolumeMacro, frameClock.GetFrame(zeroSample - 1)));
            Assert.Equal(0, MacroRunner.GetValue(curve.VolumeMacro, frameClock.GetFrame(zeroSample)));
            Assert.Equal(0, MacroRunner.GetValue(curve.VolumeMacro, frameClock.GetFrame(endSample - 1)));
            Assert.Equal(0, MacroRunner.GetValue(curve.VolumeMacro, 1000));
        }

        private static SfxEnvelopeCurve GenerateToneEnvelope(SfxEnvelopeParameters envelope)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.Nes) with
            {
                Tone = new SfxToneParameters { Envelope = envelope }
            };
            return Assert.IsType<SfxToneCurve>(SfxCurveGenerator.Generate(parameters, ChipKind.Nes).Tone).Envelope;
        }
    }
}
