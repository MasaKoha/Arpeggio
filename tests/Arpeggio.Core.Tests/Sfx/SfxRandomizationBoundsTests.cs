using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>離散機能、入力拒否、飽和と正規化後の補正境界を検証する。</summary>
    public sealed class SfxRandomizationBoundsTests
    {
        /// <summary>0のジャンプとrepeatは有効化せず、有効repeatは最小秒数で保持する。</summary>
        [Fact]
        public void Mutate_DoesNotEnableDiscreteFeaturesOrDisableRepeat()
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            SfxParameters zero = original with { Tone = original.Tone with
            {
                PitchChangeSemitones = 0, RepeatPeriodSeconds = 0
            } };
            SfxParameters result = SfxParameterRandomizer.Mutate(zero, ChipKind.Nes, 1, 1).Parameters;
            Assert.Equal(0, result.Tone.PitchChangeSemitones);
            Assert.Equal(0, result.Tone.RepeatPeriodSeconds);
            SfxParameters shortest = original with { Tone = original.Tone with { RepeatPeriodSeconds = 0.016667 } };
            SfxParameters decreased = SfxParameterRandomizer.Mutate(shortest, ChipKind.Nes, uint.MaxValue, 1).Parameters;
            Assert.Equal(0.016667, decreased.Tone.RepeatPeriodSeconds);
        }

        /// <summary>仕様の下限・上限に達した変更は再抽選せず飽和する。</summary>
        [Theory]
        [InlineData("tone.baseFrequencyHz", 20, 1u, 20)]
        [InlineData("tone.baseFrequencyHz", 12000, 8192u, 12000)]
        [InlineData("tone.slideSemitonesPerSecond", -360, 1u, -360)]
        [InlineData("tone.slideSemitonesPerSecond", 360, uint.MaxValue, 360)]
        [InlineData("tone.deltaSlideSemitonesPerSecondSquared", -1440, uint.MaxValue, -1440)]
        [InlineData("tone.deltaSlideSemitonesPerSecondSquared", 1440, 1u, 1440)]
        [InlineData("tone.vibratoDepthCents", 0, 1u, 0)]
        [InlineData("tone.vibratoDepthCents", 200, uint.MaxValue, 200)]
        [InlineData("tone.vibratoSpeedHz", 20, 1u, 20)]
        [InlineData("tone.vibratoSpeedHz", 0, 0u, 0)]
        [InlineData("tone.envelope.volume", 0, 1u, 0)]
        [InlineData("tone.envelope.volume", 15, uint.MaxValue, 15)]
        [InlineData("tone.envelope.attackSeconds", 0, 1u, 0)]
        [InlineData("tone.envelope.attackSeconds", 1, 0u, 1)]
        [InlineData("tone.envelope.decaySeconds", 0.016667, 1u, 0.016667)]
        [InlineData("tone.envelope.decaySeconds", 2, 0u, 2)]
        [InlineData("nes.noiseSlideIndicesPerSecond", 60, 1u, 60)]
        [InlineData("nes.noiseSlideIndicesPerSecond", -60, 0u, -60)]
        public void Mutate_ClampsToInputRange(string path, double value, uint seed, double expected)
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            SfxParameters boundary = SfxParameterPatch.Apply(original, ChipKind.Nes, SfxParameterTestJson.Patch(path, value)).Parameters;
            SfxParameters result = SfxParameterRandomizer.Mutate(boundary, ChipKind.Nes, seed, 1).Parameters;
            Assert.Equal(expected, SfxParameterTestJson.Read(result, path).GetDouble());
            SfxParameterValidator.Validate(result, ChipKind.Nes);
        }

        /// <summary>sustainが正規化後に0フレームになる場合だけpunchを補正する。</summary>
        [Theory]
        [InlineData(0.008333, 0.0)]
        [InlineData(0.008334, 0.089479)]
        public void Mutate_RepairsUsingNormalizedFrameBoundary(double sustain, double expectedPunch)
        {
            SfxParameters original = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            original = original with { Tone = original.Tone with { Envelope = original.Tone.Envelope with
            {
                SustainSeconds = sustain, Punch = 0
            } } };
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 1,
                new[] { "tone.envelope.sustainSeconds" });
            Assert.Equal(sustain, result.Parameters.Tone.Envelope.SustainSeconds);
            Assert.Equal(expectedPunch, result.Parameters.Tone.Envelope.Punch);
        }

        /// <summary>整数変異は正負の中間点ともゼロから遠い側へ丸める。</summary>
        [Fact]
        public void Mutate_IntegerMidpointsRoundAwayFromZero()
        {
            const uint PitchMidpointSeed = 421711719;
            const uint VolumeMidpointSeed = 1177181255;
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            original = original with { Tone = original.Tone with { PitchChangeSemitones = -7 } };
            SfxParameters pitch = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, PitchMidpointSeed, 1).Parameters;
            SfxParameters volume = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, VolumeMidpointSeed, 1).Parameters;
            Assert.Equal(-8, pitch.Tone.PitchChangeSemitones);
            Assert.Equal(11, volume.Tone.Envelope.Volume);
        }

        /// <summary>randomizeの深さ抽選も中間点を切り捨てず13centへ丸める。</summary>
        [Fact]
        public void Randomize_VibratoDepthRoundsMidpointAwayFromZero()
        {
            const uint DepthMidpointSeed = 2856220744;
            SfxParameters original = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameters result = SfxParameterRandomizer.Randomize(original, ChipKind.Nes, "jump", DepthMidpointSeed).Parameters;
            Assert.Equal(13, result.Tone.VibratoDepthCents);
        }

        /// <summary>ノイズ選択は全チップで二十一回目を使い、チップ別の幅で整数化する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, "nes.noisePeriodIndex", 10)]
        [InlineData(ChipKind.GameBoy, "gameBoy.noiseSelection", 72)]
        [InlineData(ChipKind.Snes, "snes.noiseRate", 18)]
        public void Mutate_NoiseUsesTwentyFirstDraw(ChipKind chip, string path, int expected)
        {
            const uint NoiseMidpointSeed = 70369575;
            SfxParameters original = SfxRandomizationTestData.RichParameters(chip);
            SfxParameters result = SfxParameterRandomizer.Mutate(original, chip, NoiseMidpointSeed, 1).Parameters;
            Assert.Equal(expected, SfxParameterTestJson.Read(result, path).GetInt32());
        }

        /// <summary>強度0でも不正な強度・ロック・チップ・現在値を黙認しない。</summary>
        [Fact]
        public void Operations_RejectInvalidInputsWithoutChangingCurrentValues()
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            foreach (double strength in new[] { -0.01, 1.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                SfxParameterException exception = Assert.Throws<SfxParameterException>(() =>
                    SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, strength));
                Assert.Equal("strength", exception.ParameterPath);
                Assert.Equal("InvalidParameter", exception.Code);
            }
            foreach (string path in new[] { "tone", "tone.unknown", "Tone.enabled", "gameBoy.dutyPercent", "snes.noiseRate" })
            {
                SfxParameterException exception = Assert.Throws<SfxParameterException>(() =>
                    SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 0, new[] { path }));
                Assert.Equal("UnsupportedParameter", exception.Code);
                Assert.Equal(path, exception.ParameterPath);
            }
            Assert.Throws<SfxParameterException>(() => SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 0, new[] { (string)null! }));
            Assert.Throws<SfxParameterException>(() => SfxParameterRandomizer.Randomize(original, ChipKind.Nes, "unknown", 1));
            Assert.Throws<SfxParameterException>(() => SfxParameterRandomizer.Randomize(original, ChipKind.None, "jump", 1));
            SfxParameters invalid = original with { Tone = original.Tone with { BaseFrequencyHz = double.NaN } };
            Assert.Throws<SfxParameterException>(() => SfxParameterRandomizer.Mutate(invalid, ChipKind.Nes, 1, 0));
            Assert.Equal(SfxRandomizationTestData.RichParameters(ChipKind.Nes), original);
        }
    }
}
