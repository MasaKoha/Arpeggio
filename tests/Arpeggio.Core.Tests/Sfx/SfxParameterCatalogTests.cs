using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>設計書から独立に記述した全数値行の範囲・単位・既定値と入力境界。</summary>
    public sealed class SfxParameterCatalogTests
    {
        /// <summary>仕様表の全数値行。離散選択の両端も含む。</summary>
        public static IEnumerable<object[]> NumericSpecifications()
        {
            yield return new object[] { ChipKind.Nes, "tone.baseFrequencyHz", 20.0, 12000.0, 440.0, "Hz", false };
            yield return new object[] { ChipKind.Nes, "tone.slideSemitonesPerSecond", -360.0, 360.0, 0.0, "半音/秒", false };
            yield return new object[] { ChipKind.Nes, "tone.deltaSlideSemitonesPerSecondSquared", -1440.0, 1440.0, 0.0, "半音/秒²", false };
            yield return new object[] { ChipKind.Nes, "tone.vibratoDepthCents", 0.0, 200.0, 0.0, "cent", false };
            yield return new object[] { ChipKind.Nes, "tone.vibratoSpeedHz", 0.0, 20.0, 6.0, "Hz", false };
            yield return new object[] { ChipKind.Nes, "tone.pitchChangeSemitones", -24.0, 24.0, 0.0, "半音", true };
            yield return new object[] { ChipKind.Nes, "tone.pitchChangeTimeSeconds", 0.0, 5.0, 0.05, "秒", false };
            yield return new object[] { ChipKind.Nes, "tone.repeatPeriodSeconds", 1.0 / 60, 5.0, 0.0, "秒", false };
            foreach (string layer in new[] { "tone", "noise" })
            {
                yield return new object[] { ChipKind.Nes, layer + ".envelope.volume", 0.0, 15.0, 12.0, "", true };
                yield return new object[] { ChipKind.Nes, layer + ".envelope.attackSeconds", 0.0, 1.0, 0.0, "秒", false };
                yield return new object[] { ChipKind.Nes, layer + ".envelope.sustainSeconds", 0.0, 2.0, 0.05, "秒", false };
                yield return new object[] { ChipKind.Nes, layer + ".envelope.decaySeconds", 1.0 / 60, 2.0, 0.15, "秒", false };
                yield return new object[] { ChipKind.Nes, layer + ".envelope.punch", 0.0, 1.0, 0.0, "", false };
            }
            foreach (ChipKind chip in new[] { ChipKind.Nes, ChipKind.GameBoy })
            {
                string prefix = chip == ChipKind.Nes ? "nes" : "gameBoy";
                yield return new object[] { chip, prefix + ".dutyPercent", 12.5, 75.0, 25.0, "%", false };
                yield return new object[] { chip, prefix + ".dutySweepPercentPerSecond", -100.0, 100.0, 0.0, "%ポイント/秒", false };
            }
            yield return new object[] { ChipKind.Nes, "nes.noisePeriodIndex", 0.0, 15.0, 12.0, "添字", true };
            yield return new object[] { ChipKind.Nes, "nes.noiseSlideIndicesPerSecond", -60.0, 60.0, 0.0, "添字/秒", false };
            yield return new object[] { ChipKind.GameBoy, "gameBoy.noiseWidth", 7.0, 15.0, 15.0, "bit", true };
            yield return new object[] { ChipKind.GameBoy, "gameBoy.noiseSelection", 0.0, 127.0, 96.0, "選択値", true };
            yield return new object[] { ChipKind.GameBoy, "gameBoy.noiseSlideSelectionsPerSecond", -240.0, 240.0, 0.0, "選択値/秒", false };
            yield return new object[] { ChipKind.Snes, "snes.noiseRate", 1.0, 31.0, 24.0, "rate", true };
        }

        /// <summary>全数値の両端を受理し、外側を拒否する。無効ノイズの値も検証対象。</summary>
        [Theory]
        [MemberData(nameof(NumericSpecifications))]
        public void NumericParameters_MatchSpecificationAndEnforceBoundaries(ChipKind chip, string path,
            double minimum, double maximum, double defaultValue, string unit, bool isInteger)
        {
            SfxParameterDescription description = SfxParameterCatalog.Get(path, chip);
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(chip);
            Assert.Equal(minimum, description.Minimum);
            Assert.Equal(maximum, description.Maximum);
            Assert.Equal(unit, description.Unit);
            Assert.Equal(defaultValue, Convert.ToDouble(description.DefaultValue));
            Assert.Equal(defaultValue, SfxParameterTestJson.Read(initial, path).GetDouble());
            Assert.Equal(isInteger ? SfxParameterValueKind.Integer : SfxParameterValueKind.Number, description.ValueKind);
            foreach (double boundary in new[] { minimum, maximum })
            {
                string patch = SfxParameterTestJson.Patch(path, boundary);
                SfxParameterPatchResult result = SfxParameterPatch.Apply(initial, chip, patch);
                Assert.Equal(Math.Round(boundary, 6, MidpointRounding.AwayFromZero), SfxParameterTestJson.Read(result.Parameters, path).GetDouble());
            }
            const double OutsideDistance = 0.000001;
            foreach (double outside in new[] { minimum - OutsideDistance, maximum + OutsideDistance })
            {
                SfxParameterException exception = Assert.Throws<SfxParameterException>(() =>
                    SfxParameterPatch.Apply(initial, chip, SfxParameterTestJson.Patch(path, outside)));
                Assert.Equal("InvalidParameter", exception.Code);
                Assert.Equal(path, exception.ParameterPath);
            }
        }

        /// <summary>全32項目が一意で、共通20項目と各チップの専用項目だけを公開する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 25)]
        [InlineData(ChipKind.GameBoy, 25)]
        [InlineData(ChipKind.Snes, 22)]
        public void Catalog_ExposesOnlySupportedParameters(ChipKind chip, int expectedCount)
        {
            IReadOnlyList<SfxParameterDescription> all = SfxParameterCatalog.GetAll();
            Assert.Equal(32, all.Count);
            Assert.Equal(all.Count, all.Select(description => description.Path).Distinct().Count());
            Assert.Equal(expectedCount, SfxParameterCatalog.GetAll(chip).Count);
            Assert.Empty(SfxParameterValidator.Validate(SfxParameterCatalog.CreateDefaults(chip), chip));
            foreach (SfxParameterDescription description in all)
            {
                Assert.False(string.IsNullOrWhiteSpace(description.Description));
                Assert.True(description.Step > 0);
                Assert.True(description.FineStep > 0);
                bool supported = description.Chip == ChipKind.None || description.Chip == chip;
                Assert.Equal(supported, description.IsSupported(chip));
                Assert.False(description.IsSupported(ChipKind.None));
                if (!supported)
                {
                    Assert.Throws<SfxParameterException>(() => SfxParameterCatalog.Get(description.Path, chip));
                }
            }
        }

        /// <summary>Hz の対数操作と時間・整数・選択の刻みを入力制限と区別する。</summary>
        [Fact]
        public void Catalog_DescribesStepsAndDiscreteChoices()
        {
            SfxParameterDescription frequency = SfxParameterCatalog.Get("tone.baseFrequencyHz", ChipKind.Nes);
            Assert.True(frequency.IsLogarithmic);
            Assert.Equal("半音", frequency.StepUnit);
            Assert.Equal(1, frequency.Step);
            Assert.Equal(0.01, frequency.FineStep);
            Assert.Equal(1.0 / 60, SfxParameterCatalog.Get("tone.envelope.attackSeconds", ChipKind.Nes).Step);
            Assert.Equal(0.1, SfxParameterCatalog.Get("tone.vibratoSpeedHz", ChipKind.Nes).Step);
            Assert.Equal(0.01, SfxParameterCatalog.Get("noise.envelope.punch", ChipKind.Nes).Step);
            Assert.True(SfxParameterCatalog.Get("tone.repeatPeriodSeconds", ChipKind.Nes).AllowsZero);
            Assert.Single(SfxParameterCatalog.GetAll(), description => description.AllowsZero);
            Assert.Equal(new object[] { 12.5, 25.0, 50.0, 75.0 }, SfxParameterCatalog.Get("nes.dutyPercent", ChipKind.Nes).Choices);
            Assert.Equal(new object[] { 12.5, 25.0, 50.0, 75.0 }, SfxParameterCatalog.Get("gameBoy.dutyPercent", ChipKind.GameBoy).Choices);
            Assert.Equal(new object[] { 7, 15 }, SfxParameterCatalog.Get("gameBoy.noiseWidth", ChipKind.GameBoy).Choices);
            Assert.Equal(new object[] { "long", "short" }, SfxParameterCatalog.Get("nes.noiseMode", ChipKind.Nes).Choices);
            Assert.Equal(new object[] { "sine", "square", "saw", "triangle", "pulse" }, SfxParameterCatalog.Get("snes.waveform", ChipKind.Snes).Choices);
            Assert.Equal("long", SfxParameterCatalog.Get("nes.noiseMode", ChipKind.Nes).DefaultValue);
            Assert.Equal("pulse", SfxParameterCatalog.Get("snes.waveform", ChipKind.Snes).DefaultValue);
            Assert.True(Assert.IsType<bool>(SfxParameterCatalog.Get("tone.enabled", ChipKind.Nes).DefaultValue));
            Assert.False(Assert.IsType<bool>(SfxParameterCatalog.Get("noise.enabled", ChipKind.Nes).DefaultValue));
        }
    }
}
