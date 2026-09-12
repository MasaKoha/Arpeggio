using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Parameters
{
    /// <summary>値オブジェクトからの不正入力と、正規化・包絡・レイヤー間の制約。</summary>
    public sealed class SfxParameterValidatorTests
    {
        /// <summary>不明チップ・固有設定不足・別チップ混在・不正 enum を拒否する。</summary>
        [Fact]
        public void Validate_RejectsInvalidStructureAndChipSettings()
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            AssertInvalid(initial, ChipKind.None, "chip");
            AssertInvalid(initial, (ChipKind)99, "chip");
            Assert.Throws<SfxParameterException>(() => SfxParameterCatalog.CreateDefaults(ChipKind.None));
            Assert.Throws<SfxParameterException>(() => SfxParameterCatalog.GetAll(ChipKind.None));
            AssertInvalid(null!, ChipKind.Nes, "");
            AssertInvalid(initial with { Tone = null! }, ChipKind.Nes, "tone");
            AssertInvalid(initial with { Noise = null! }, ChipKind.Nes, "noise");
            AssertInvalid(initial with { Tone = initial.Tone with { Envelope = null! } }, ChipKind.Nes, "tone.envelope");
            AssertInvalid(initial with { Noise = initial.Noise with { Envelope = null! } }, ChipKind.Nes, "noise.envelope");
            AssertInvalid(initial with { Nes = null }, ChipKind.Nes, "nes");
            AssertInvalid(new SfxParameters(), ChipKind.GameBoy, "gameBoy");
            AssertInvalid(new SfxParameters(), ChipKind.Snes, "snes");
            SfxParameterException mixed = Assert.Throws<SfxParameterException>(() => SfxParameterValidator.Validate(
                initial with { GameBoy = new SfxGameBoyParameters() }, ChipKind.Nes));
            Assert.Equal("UnsupportedParameter", mixed.Code);
            Assert.Equal("gameBoy", mixed.ParameterPath);
            AssertInvalid(initial with { Nes = new SfxNesParameters { NoiseMode = NoiseMode.None } }, ChipKind.Nes, "nes.noiseMode");
            AssertInvalid(initial with { Nes = new SfxNesParameters { NoiseMode = (NoiseMode)99 } }, ChipKind.Nes, "nes.noiseMode");
            AssertInvalid(new SfxParameters { Snes = new SfxSnesParameters { Waveform = SnesWaveformKind.Noise } }, ChipKind.Snes, "snes.waveform");
            AssertInvalid(new SfxParameters { Snes = new SfxSnesParameters { Waveform = SnesWaveformKind.None } }, ChipKind.Snes, "snes.waveform");
        }

        /// <summary>全レイヤー OFF はエラー。無音は有効レイヤーの音量だけで判断する。</summary>
        [Fact]
        public void Validate_DistinguishesDisabledLayersFromSilentParameters()
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            AssertInvalid(initial with { Tone = initial.Tone with { Enabled = false } }, ChipKind.Nes, "tone.enabled");
            SfxParameters silentTone = initial with { Tone = initial.Tone with { Envelope = new SfxEnvelopeParameters { Volume = 0 } } };
            SfxParameterWarning warning = Assert.Single(SfxParameterValidator.Validate(silentTone, ChipKind.Nes));
            Assert.Equal("SilentParameters", warning.Code);
            Assert.Equal(string.Empty, warning.ParameterPath);
            Assert.Empty(SfxParameterValidator.Validate(silentTone with { Noise = initial.Noise with { Enabled = true } }, ChipKind.Nes));
            SfxParameters noiseOnly = initial with
            {
                Tone = initial.Tone with { Enabled = false },
                Noise = initial.Noise with { Enabled = true, Envelope = new SfxEnvelopeParameters { Volume = 0 } }
            };
            Assert.Equal("SilentParameters", Assert.Single(SfxParameterValidator.Validate(noiseOnly, ChipKind.Nes)).Code);
            AssertInvalid(initial with { Noise = initial.Noise with { Envelope = new SfxEnvelopeParameters { Volume = -1 } } },
                ChipKind.Nes, "noise.envelope.volume");
        }

        /// <summary>punch の条件は正規化後の保持フレームで判定し、無効レイヤーにも適用する。</summary>
        [Theory]
        [InlineData("tone", 0, false)]
        [InlineData("tone", 0.008333, false)]
        [InlineData("tone", 1.0 / 120, false)]
        [InlineData("tone", 0.0083335, true)]
        [InlineData("tone", 0.008334, true)]
        [InlineData("noise", 0, false)]
        [InlineData("noise", 0.008333, false)]
        [InlineData("noise", 0.008334, true)]
        public void Validate_PunchRequiresQuantizedSustain(string layer, double sustainSeconds, bool valid)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxEnvelopeParameters envelope = new SfxEnvelopeParameters { SustainSeconds = sustainSeconds, Punch = 1 };
            SfxParameters candidate = layer == "tone"
                ? initial with { Tone = initial.Tone with { Envelope = envelope } }
                : initial with { Noise = initial.Noise with { Envelope = envelope } };
            if (valid)
            {
                Assert.Empty(SfxParameterValidator.Validate(candidate, ChipKind.Nes));
                return;
            }
            AssertInvalid(candidate, ChipKind.Nes, layer + ".envelope.punch");
        }

        /// <summary>各包絡は独立に最大300フレームを受理し、要求秒数を保持する。</summary>
        [Fact]
        public void Normalize_PreservesIndependentEnvelopeTimesAndMaximum()
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.GameBoy);
            SfxParameters candidate = initial with
            {
                Tone = initial.Tone with { Envelope = new SfxEnvelopeParameters { AttackSeconds = 1, SustainSeconds = 2, DecaySeconds = 2, Punch = 1 } },
                Noise = initial.Noise with { Enabled = true, Envelope = new SfxEnvelopeParameters { AttackSeconds = 0, SustainSeconds = 0, DecaySeconds = 1.0 / 60 } }
            };
            SfxParameters normalized = SfxParameterValidator.Normalize(candidate, ChipKind.GameBoy);
            Assert.Equal(1, normalized.Tone.Envelope.AttackSeconds);
            Assert.Equal(2, normalized.Tone.Envelope.SustainSeconds);
            Assert.Equal(2, normalized.Tone.Envelope.DecaySeconds);
            Assert.Equal(0.016667, normalized.Noise.Envelope.DecaySeconds);
            Assert.Equal(1.0 / 60, candidate.Noise.Envelope.DecaySeconds);
            Assert.Equal(normalized, SfxParameterValidator.Normalize(normalized, ChipKind.GameBoy));
            AssertInvalid(candidate with { Tone = candidate.Tone with { Envelope = candidate.Tone.Envelope with { AttackSeconds = 1.0000001 } } },
                ChipKind.GameBoy, "tone.envelope.attackSeconds");
        }

        /// <summary>repeat のゼロと最小正値は受理し、間の値や丸め前の範囲外を拒否する。</summary>
        [Theory]
        [InlineData(0, true)]
        [InlineData(1.0 / 60, true)]
        [InlineData(0.016667, true)]
        [InlineData(5, true)]
        [InlineData(-0.0000001, false)]
        [InlineData(0.0000001, false)]
        [InlineData(0.0166666, false)]
        [InlineData(5.0000001, false)]
        public void Validate_RepeatHasDisjointRange(double seconds, bool valid)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameters candidate = initial with { Tone = initial.Tone with { RepeatPeriodSeconds = seconds } };
            if (valid)
            {
                Assert.Empty(SfxParameterValidator.Validate(candidate, ChipKind.Nes));
                return;
            }
            AssertInvalid(candidate, ChipKind.Nes, "tone.repeatPeriodSeconds");
        }

        /// <summary>丸めは正負とも AwayFromZero。負ゼロも正規化し、入力を変更しない。</summary>
        [Theory]
        [InlineData(1.2345675, 1.234568)]
        [InlineData(-1.2345675, -1.234568)]
        [InlineData(0.0000005, 0.000001)]
        [InlineData(-0.0000005, -0.000001)]
        [InlineData(-0.0, 0.0)]
        public void Normalize_UsesSixDecimalPlaces(double value, double expected)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameters candidate = initial with { Tone = initial.Tone with { SlideSemitonesPerSecond = value } };
            SfxParameters normalized = SfxParameterValidator.Normalize(candidate, ChipKind.Nes);
            Assert.Equal(expected, normalized.Tone.SlideSemitonesPerSecond);
            Assert.Equal(value, candidate.Tone.SlideSemitonesPerSecond);
            if (expected == 0)
            {
                Assert.Equal(0L, BitConverter.DoubleToInt64Bits(normalized.Tone.SlideSemitonesPerSecond));
            }
        }

        /// <summary>JSON を経由しない全実数項目も NaN と正負 Infinity を拒否する。</summary>
        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Validate_RejectsNonFiniteValuesInEveryRealParameter(double value)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            AssertInvalid(initial with { Tone = initial.Tone with { BaseFrequencyHz = value } }, ChipKind.Nes, "tone.baseFrequencyHz");
            AssertInvalid(initial with { Tone = initial.Tone with { SlideSemitonesPerSecond = value } }, ChipKind.Nes, "tone.slideSemitonesPerSecond");
            AssertInvalid(initial with { Tone = initial.Tone with { DeltaSlideSemitonesPerSecondSquared = value } }, ChipKind.Nes, "tone.deltaSlideSemitonesPerSecondSquared");
            AssertInvalid(initial with { Tone = initial.Tone with { VibratoDepthCents = value } }, ChipKind.Nes, "tone.vibratoDepthCents");
            AssertInvalid(initial with { Tone = initial.Tone with { VibratoSpeedHz = value } }, ChipKind.Nes, "tone.vibratoSpeedHz");
            AssertInvalid(initial with { Tone = initial.Tone with { PitchChangeTimeSeconds = value } }, ChipKind.Nes, "tone.pitchChangeTimeSeconds");
            AssertInvalid(initial with { Tone = initial.Tone with { RepeatPeriodSeconds = value } }, ChipKind.Nes, "tone.repeatPeriodSeconds");
            AssertInvalid(initial with { Tone = initial.Tone with { Envelope = initial.Tone.Envelope with { AttackSeconds = value } } },
                ChipKind.Nes, "tone.envelope.attackSeconds");
            AssertInvalid(initial with { Tone = initial.Tone with { Envelope = initial.Tone.Envelope with { SustainSeconds = value } } },
                ChipKind.Nes, "tone.envelope.sustainSeconds");
            AssertInvalid(initial with { Tone = initial.Tone with { Envelope = initial.Tone.Envelope with { DecaySeconds = value } } },
                ChipKind.Nes, "tone.envelope.decaySeconds");
            AssertInvalid(initial with { Tone = initial.Tone with { Envelope = initial.Tone.Envelope with { Punch = value } } },
                ChipKind.Nes, "tone.envelope.punch");
            AssertInvalid(initial with { Noise = initial.Noise with { Envelope = initial.Noise.Envelope with { AttackSeconds = value } } },
                ChipKind.Nes, "noise.envelope.attackSeconds");
            AssertInvalid(initial with { Noise = initial.Noise with { Envelope = initial.Noise.Envelope with { SustainSeconds = value } } },
                ChipKind.Nes, "noise.envelope.sustainSeconds");
            AssertInvalid(initial with { Noise = initial.Noise with { Envelope = initial.Noise.Envelope with { DecaySeconds = value } } },
                ChipKind.Nes, "noise.envelope.decaySeconds");
            AssertInvalid(initial with { Noise = initial.Noise with { Envelope = initial.Noise.Envelope with { Punch = value } } },
                ChipKind.Nes, "noise.envelope.punch");
            AssertInvalid(initial with { Nes = new SfxNesParameters { DutyPercent = value } }, ChipKind.Nes, "nes.dutyPercent");
            AssertInvalid(initial with { Nes = new SfxNesParameters { DutySweepPercentPerSecond = value } }, ChipKind.Nes, "nes.dutySweepPercentPerSecond");
            AssertInvalid(initial with { Nes = new SfxNesParameters { NoiseSlideIndicesPerSecond = value } }, ChipKind.Nes, "nes.noiseSlideIndicesPerSecond");
            AssertInvalid(new SfxParameters { GameBoy = new SfxGameBoyParameters { DutyPercent = value } }, ChipKind.GameBoy, "gameBoy.dutyPercent");
            AssertInvalid(new SfxParameters { GameBoy = new SfxGameBoyParameters { DutySweepPercentPerSecond = value } }, ChipKind.GameBoy, "gameBoy.dutySweepPercentPerSecond");
            AssertInvalid(new SfxParameters { GameBoy = new SfxGameBoyParameters { NoiseSlideSelectionsPerSecond = value } }, ChipKind.GameBoy, "gameBoy.noiseSlideSelectionsPerSecond");
        }

        private static void AssertInvalid(SfxParameters parameters, ChipKind chip, string path)
        {
            SfxParameterException exception = Assert.Throws<SfxParameterException>(() => SfxParameterValidator.Validate(parameters, chip));
            Assert.Equal("InvalidParameter", exception.Code);
            Assert.Equal(path, exception.ParameterPath);
        }
    }
}
