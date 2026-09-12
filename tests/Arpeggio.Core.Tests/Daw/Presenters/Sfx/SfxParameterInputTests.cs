using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Daw.Presenters.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>対数操作・整数境界・部分patchの変換契約を守る。</summary>
    public sealed class SfxParameterInputTests
    {
        /// <summary>周波数の通常キーは半音、Shiftは1centであり、Hzの加算にならない。</summary>
        [Fact]
        public void FrequencyUsesSemitonesAndCentsAndRoundTripsLogSlider()
        {
            SfxParameterDescription frequency = SfxParameterCatalog.Get("tone.baseFrequencyHz", ChipKind.Nes);
            Assert.Equal(466.163762, Assert.IsType<double>(SfxParameterInput.Step(frequency, 440.0, 1, false)));
            Assert.Equal(440.254227, Assert.IsType<double>(SfxParameterInput.Step(frequency, 440.0, 1, true)));
            Assert.Equal(20.0, SfxParameterInput.Step(frequency, 20.0, -1, true));
            Assert.Equal(12000.0, SfxParameterInput.Step(frequency, 12000.0, 1, false));
            Assert.Equal(440.0, SfxParameterInput.FromSlider(frequency, SfxParameterInput.ToSlider(frequency, 440)));
        }

        /// <summary>反復のゼロと最小有効値の間に、キー操作で不正な中間値を作らない。</summary>
        [Fact]
        public void RepeatSkipsForbiddenGapAndIntegerFineStepRemainsOne()
        {
            SfxParameterDescription repeat = SfxParameterCatalog.Get("tone.repeatPeriodSeconds", ChipKind.Nes);
            Assert.Equal(0.016667, SfxParameterInput.Step(repeat, 0.0, 1, true));
            Assert.Equal(0.0, SfxParameterInput.Step(repeat, 0.016667, -1, true));
            Assert.Equal(0.0, SfxParameterInput.FromSlider(repeat, 0.001));
            Assert.Equal(0.016667, SfxParameterInput.FromSlider(repeat, 0.01));
            SfxParameterDescription volume = SfxParameterCatalog.Get("tone.envelope.volume", ChipKind.Nes);
            Assert.Equal(13.0, SfxParameterInput.Step(volume, 12.0, 1, true));
            Assert.Equal(15.0, SfxParameterInput.Step(volume, 15.0, 1, true));
        }

        /// <summary>離散dutyは許可された隣接値へ進み、Shiftでも中間値を作らない。</summary>
        [Fact]
        public void ChoiceStepsRemainInCatalogIncludingNumericChoices()
        {
            SfxParameterDescription duty = SfxParameterCatalog.Get("nes.dutyPercent", ChipKind.Nes);
            Assert.Equal(50.0, SfxParameterInput.Step(duty, 25.0, 1, true));
            Assert.Equal(12.5, SfxParameterInput.Step(duty, 12.5, -1, false));
            SfxParameterDescription waveform = SfxParameterCatalog.Get("snes.waveform", ChipKind.Snes);
            Assert.Equal("square", SfxParameterInput.Step(waveform, "sine", 1, true));
        }

        /// <summary>複数パスは一patchになり、小数カンマ環境でも同じ値を適用する。</summary>
        [Fact]
        public void PatchCombinesNestedValuesWithoutUsingCurrentCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ",";
            try
            {
                CultureInfo.CurrentCulture = culture;
                string patch = SfxParameterInput.CreatePatch(new Dictionary<string, object>
                {
                    ["tone.baseFrequencyHz"] = SfxParameterInput.ParseText("523.251131"),
                    ["tone.envelope.volume"] = SfxParameterInput.ParseText("1.3e1"),
                    ["noise.enabled"] = true,
                    ["nes.noiseMode"] = "short"
                });
                SfxParameters parameters = SfxParameterPatch.Apply(SfxParameterCatalog.CreateDefaults(ChipKind.Nes), ChipKind.Nes, patch).Parameters;
                Assert.Equal(523.251131, parameters.Tone.BaseFrequencyHz);
                Assert.Equal(13, parameters.Tone.Envelope.Volume);
                Assert.True(parameters.Noise.Enabled);
                Assert.Equal("523.251131", SfxParameterInput.Format(parameters.Tone.BaseFrequencyHz));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        /// <summary>指数のアンダーフローや整数端数を丸めて有効値に変えない。</summary>
        [Theory]
        [InlineData("tone.repeatPeriodSeconds", "1e-999")]
        [InlineData("tone.envelope.volume", "12.00000000000000001")]
        public void ExactNumericTokensReachCoreValidation(string path, string text)
        {
            string patch = SfxParameterInput.CreatePatch(new Dictionary<string, object> { [path] = SfxParameterInput.ParseText(text) });
            Assert.Throws<SfxParameterException>(() => SfxParameterPatch.Apply(SfxParameterCatalog.CreateDefaults(ChipKind.Nes), ChipKind.Nes, patch));
        }

        /// <summary>不正文字列はJSONを壊さず、Coreで該当パスの入力エラーになる。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("440,5")]
        [InlineData("\"bad\"")]
        public void InvalidTextIsRetainedAndRejectedAtItsPath(string text)
        {
            string patch = SfxParameterInput.CreatePatch(new Dictionary<string, object>
            {
                ["tone.baseFrequencyHz"] = SfxParameterInput.ParseText(text)
            });
            SfxParameterException exception = Assert.Throws<SfxParameterException>(() =>
                SfxParameterPatch.Apply(SfxParameterCatalog.CreateDefaults(ChipKind.Nes), ChipKind.Nes, patch));
            Assert.Equal("tone.baseFrequencyHz", exception.ParameterPath);
        }
    }
}
