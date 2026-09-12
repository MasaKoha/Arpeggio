using System;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Parameters
{
    /// <summary>部分更新・構造エラー・正式パス・原子性と正規化後の同値判定。</summary>
    public sealed class SfxParameterPatchTests
    {
        /// <summary>指定した末端値だけを変更し、他の包絡・チップ値と元入力を保持する。</summary>
        [Fact]
        public void Apply_PreservesOmittedValuesAndOriginal()
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameters expected = initial with
            {
                Tone = initial.Tone with
                {
                    SlideSemitonesPerSecond = -12,
                    Envelope = initial.Tone.Envelope with { DecaySeconds = 0.2 }
                }
            };
            SfxParameterPatchResult result = SfxParameterPatch.Apply(initial, ChipKind.Nes,
                "{\"tone\":{\"slideSemitonesPerSecond\":-12,\"envelope\":{\"decaySeconds\":0.2}}}");
            Assert.True(result.Changed);
            Assert.Equal(expected, result.Parameters);
            Assert.Equal(SfxParameterCatalog.CreateDefaults(ChipKind.Nes), initial);
            Assert.Empty(result.Warnings);
        }

        /// <summary>全項目の同時更新は JSON の指定順に依存せず、強い型へ対応する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Apply_FullPatchMapsEveryProperty(ChipKind chip)
        {
            const string Common = "\"noise\":{\"envelope\":{\"punch\":0.5,\"decaySeconds\":0.3,\"sustainSeconds\":0.2,\"attackSeconds\":0.1,\"volume\":9},\"enabled\":true},"
                + "\"tone\":{\"envelope\":{\"volume\":10,\"attackSeconds\":0.2,\"sustainSeconds\":0.3,\"decaySeconds\":0.4,\"punch\":0.25},"
                + "\"repeatPeriodSeconds\":0.1,\"pitchChangeTimeSeconds\":0.02,\"pitchChangeSemitones\":-7,\"vibratoSpeedHz\":8.5,"
                + "\"vibratoDepthCents\":35,\"deltaSlideSemitonesPerSecondSquared\":24,\"slideSemitonesPerSecond\":-12,\"baseFrequencyHz\":523.251131,\"enabled\":false}";
            string specific = chip switch
            {
                ChipKind.Nes => "\"nes\":{\"noiseSlideIndicesPerSecond\":-30,\"noisePeriodIndex\":3,\"noiseMode\":\"short\",\"dutySweepPercentPerSecond\":12,\"dutyPercent\":12.5}",
                ChipKind.GameBoy => "\"gameBoy\":{\"noiseSlideSelectionsPerSecond\":-80,\"noiseSelection\":120,\"noiseWidth\":7,\"dutySweepPercentPerSecond\":12,\"dutyPercent\":12.5}",
                _ => "\"snes\":{\"noiseRate\":31,\"waveform\":\"triangle\"}"
            };
            SfxParameters expected = SfxParameterCatalog.CreateDefaults(chip) with
            {
                Tone = new SfxToneParameters
                {
                    Enabled = false, BaseFrequencyHz = 523.251131, SlideSemitonesPerSecond = -12,
                    DeltaSlideSemitonesPerSecondSquared = 24, VibratoDepthCents = 35, VibratoSpeedHz = 8.5,
                    PitchChangeSemitones = -7, PitchChangeTimeSeconds = 0.02, RepeatPeriodSeconds = 0.1,
                    Envelope = new SfxEnvelopeParameters { Volume = 10, AttackSeconds = 0.2, SustainSeconds = 0.3, DecaySeconds = 0.4, Punch = 0.25 }
                },
                Noise = new SfxNoiseParameters
                {
                    Enabled = true,
                    Envelope = new SfxEnvelopeParameters { Volume = 9, AttackSeconds = 0.1, SustainSeconds = 0.2, DecaySeconds = 0.3, Punch = 0.5 }
                }
            };
            expected = chip switch
            {
                ChipKind.Nes => expected with { Nes = new SfxNesParameters { DutyPercent = 12.5, DutySweepPercentPerSecond = 12, NoiseMode = NoiseMode.Short, NoisePeriodIndex = 3, NoiseSlideIndicesPerSecond = -30 } },
                ChipKind.GameBoy => expected with { GameBoy = new SfxGameBoyParameters { DutyPercent = 12.5, DutySweepPercentPerSecond = 12, NoiseWidth = 7, NoiseSelection = 120, NoiseSlideSelectionsPerSecond = -80 } },
                _ => expected with { Snes = new SfxSnesParameters { Waveform = SnesWaveformKind.Triangle, NoiseRate = 31 } }
            };
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(chip);
            SfxParameterPatchResult result = SfxParameterPatch.Apply(initial, chip, "{" + Common + "," + specific + "}");
            SfxParameterPatchResult reversed = SfxParameterPatch.Apply(initial, chip, "{" + specific + "," + Common + "}");
            Assert.Equal(expected, result.Parameters);
            Assert.Equal(expected, reversed.Parameters);
            Assert.True(result.Changed);
        }

        /// <summary>未知・重複・null・配列・型違い・不正選択を安定コードと位置付きで拒否する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, "", "InvalidParameter", "")]
        [InlineData(ChipKind.Nes, "{}", "InvalidParameter", "")]
        [InlineData(ChipKind.Nes, "[]", "InvalidParameter", "")]
        [InlineData(ChipKind.Nes, "null", "InvalidParameter", "")]
        [InlineData(ChipKind.Nes, "true", "InvalidParameter", "")]
        [InlineData(ChipKind.Nes, "{", "InvalidParameter", "")]
        [InlineData(ChipKind.Nes, "{\"tone\":{}}", "InvalidParameter", "tone")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"envelope\":{}}}", "InvalidParameter", "tone.envelope")]
        [InlineData(ChipKind.Nes, "{\"tone\":null}", "InvalidParameter", "tone")]
        [InlineData(ChipKind.Nes, "{\"tone\":[]}", "InvalidParameter", "tone")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"envelope\":null}}", "InvalidParameter", "tone.envelope")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":null}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":\"440\"}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":true}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":{\"value\":440}}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":[440]}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":1e400}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":\"NaN\"}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":\"Infinity\"}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"enabled\":\"true\"}}", "InvalidParameter", "tone.enabled")]
        [InlineData(ChipKind.Nes, "{\"noise\":{\"enabled\":1}}", "InvalidParameter", "noise.enabled")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"envelope\":{\"volume\":12.5}}}", "InvalidParameter", "tone.envelope.volume")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"pitchChangeSemitones\":1.5}}", "InvalidParameter", "tone.pitchChangeSemitones")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":440,\"baseFrequencyHz\":880}}", "InvalidParameter", "tone.baseFrequencyHz")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"baseFrequencyHz\":440},\"tone\":{\"vibratoSpeedHz\":4}}", "InvalidParameter", "tone")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"enabled\":true,\"enabl\\u0065d\":true}}", "InvalidParameter", "tone.enabled")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"envelope\":{\"volume\":1,\"volume\":2}}}", "InvalidParameter", "tone.envelope.volume")]
        [InlineData(ChipKind.Nes, "{\"tone\":{\"frequency\":440}}", "UnsupportedParameter", "tone.frequency")]
        [InlineData(ChipKind.Nes, "{\"Tone\":{\"enabled\":true}}", "UnsupportedParameter", "Tone")]
        [InlineData(ChipKind.Nes, "{\"tone.enabled\":true}", "UnsupportedParameter", "tone.enabled")]
        [InlineData(ChipKind.Nes, "{\"parameters\":{\"tone\":{\"enabled\":true}}}", "UnsupportedParameter", "parameters")]
        [InlineData(ChipKind.Nes, "{\"chip\":\"snes\"}", "UnsupportedParameter", "chip")]
        [InlineData(ChipKind.Nes, "{\"gameBoy\":{\"dutyPercent\":25}}", "UnsupportedParameter", "gameBoy")]
        [InlineData(ChipKind.GameBoy, "{\"nes\":{\"noiseMode\":\"long\"}}", "UnsupportedParameter", "nes")]
        [InlineData(ChipKind.Snes, "{\"snes\":{\"dutyPercent\":25}}", "UnsupportedParameter", "snes.dutyPercent")]
        [InlineData(ChipKind.Snes, "{\"snes\":{\"noiseSlideIndicesPerSecond\":1}}", "UnsupportedParameter", "snes.noiseSlideIndicesPerSecond")]
        [InlineData(ChipKind.Snes, "{\"snes\":{\"waveform\":\"noise\"}}", "InvalidParameter", "snes.waveform")]
        [InlineData(ChipKind.Snes, "{\"snes\":{\"waveform\":\"none\"}}", "InvalidParameter", "snes.waveform")]
        [InlineData(ChipKind.Snes, "{\"snes\":{\"waveform\":1}}", "InvalidParameter", "snes.waveform")]
        [InlineData(ChipKind.Nes, "{\"nes\":{\"noiseMode\":\"Long\"}}", "InvalidParameter", "nes.noiseMode")]
        [InlineData(ChipKind.Nes, "{\"nes\":{\"noiseMode\":\"1\"}}", "InvalidParameter", "nes.noiseMode")]
        [InlineData(ChipKind.Nes, "{\"nes\":{\"dutyPercent\":30}}", "InvalidParameter", "nes.dutyPercent")]
        [InlineData(ChipKind.GameBoy, "{\"gameBoy\":{\"noiseWidth\":8}}", "InvalidParameter", "gameBoy.noiseWidth")]
        public void Apply_RejectsInvalidInput(ChipKind chip, string json, string code, string path)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(chip);
            SfxParameterException exception = Assert.Throws<SfxParameterException>(() => SfxParameterPatch.Apply(initial, chip, json));
            Assert.Equal(code, exception.Code);
            Assert.Equal(path, exception.ParameterPath);
            Assert.Equal(SfxParameterCatalog.CreateDefaults(chip), initial);
        }

        /// <summary>同じ patch 内で両レイヤーを切り替えても途中状態を検証しない。</summary>
        [Theory]
        [InlineData("{\"tone\":{\"enabled\":false},\"noise\":{\"enabled\":true}}")]
        [InlineData("{\"noise\":{\"enabled\":true},\"tone\":{\"enabled\":false}}")]
        public void Apply_ValidatesCompleteCandidate(string patch)
        {
            SfxParameterPatchResult result = SfxParameterPatch.Apply(SfxParameterCatalog.CreateDefaults(ChipKind.Nes), ChipKind.Nes, patch);
            Assert.False(result.Parameters.Tone.Enabled);
            Assert.True(result.Parameters.Noise.Enabled);
            Assert.Empty(result.Warnings);
        }

        /// <summary>途中に正しい変更があっても、後続の値エラーで元パラメータを変更しない。</summary>
        [Fact]
        public void Apply_FailureDoesNotPublishPartialChanges()
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            const string Invalid = "{\"tone\":{\"baseFrequencyHz\":880,\"envelope\":{\"sustainSeconds\":0,\"punch\":1}}}";
            Assert.Throws<SfxParameterException>(() => SfxParameterPatch.Apply(initial, ChipKind.Nes, Invalid));
            Assert.Equal(440, initial.Tone.BaseFrequencyHz);
            Assert.Equal(0.05, initial.Tone.Envelope.SustainSeconds);
            Assert.Equal(0, initial.Tone.Envelope.Punch);
        }

        /// <summary>UI 刻みより細かい数値と指数表記を受理し、6桁を超える差は no-op にする。</summary>
        [Fact]
        public void Apply_NormalizesWithoutQuantizingSecondsToFrames()
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameterPatchResult unchanged = SfxParameterPatch.Apply(initial, ChipKind.Nes,
                "{\"tone\":{\"baseFrequencyHz\":440.0000004,\"slideSemitonesPerSecond\":-0.0000001}}");
            Assert.False(unchanged.Changed);
            Assert.Equal(initial, unchanged.Parameters);
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(unchanged.Parameters.Tone.SlideSemitonesPerSecond));
            SfxParameterPatchResult changed = SfxParameterPatch.Apply(initial, ChipKind.Nes,
                "{\"tone\":{\"baseFrequencyHz\":5.23251131e2,\"slideSemitonesPerSecond\":-1.2345675,\"envelope\":{\"attackSeconds\":0.0100014}}}");
            Assert.Equal(523.251131, changed.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(-1.234568, changed.Parameters.Tone.SlideSemitonesPerSecond);
            Assert.Equal(0.010001, changed.Parameters.Tone.Envelope.AttackSeconds);
            Assert.True(changed.Changed);
            Assert.False(SfxParameterPatch.Apply(changed.Parameters, ChipKind.Nes,
                "{\"tone\":{\"slideSemitonesPerSecond\":-1.234568}} ").Changed);
        }

        /// <summary>全末端パスで null と配列を拒否し、真偽値・数値・文字列を暗黙変換しない。</summary>
        [Fact]
        public void Apply_RejectsNullArraysAndWrongTypesForEveryParameter()
        {
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll())
            {
                ChipKind chip = description.Chip == ChipKind.None ? ChipKind.Nes : description.Chip;
                SfxParameters initial = SfxParameterCatalog.CreateDefaults(chip);
                object wrongType = description.ValueKind == SfxParameterValueKind.Boolean ? "true" : true;
                foreach (object value in new object[] { null!, new[] { 1 }, wrongType })
                {
                    SfxParameterException exception = Assert.Throws<SfxParameterException>(() => SfxParameterPatch.Apply(initial, chip,
                        SfxParameterTestJson.Patch(description.Path, value)));
                    Assert.Equal("InvalidParameter", exception.Code);
                    Assert.Equal(description.Path, exception.ParameterPath);
                }
            }
        }

        /// <summary>整数値の小数・指数表記は受理し、double の丸めやアンダーフローで小数を整数にしない。</summary>
        [Theory]
        [InlineData("12.0", true, 12)]
        [InlineData("1.2e1", true, 12)]
        [InlineData("120e-1", true, 12)]
        [InlineData("0.0e-400", true, 0)]
        [InlineData("12.0000000000000001", false, 0)]
        [InlineData("1e-400", false, 0)]
        [InlineData("1e-2147483648", false, 0)]
        [InlineData("1e-99999999999", false, 0)]
        public void Apply_RequiresMathematicallyIntegralValues(string token, bool valid, int expected)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            string patch = "{\"tone\":{\"envelope\":{\"volume\":" + token + "}}}";
            if (valid)
            {
                Assert.Equal(expected, SfxParameterPatch.Apply(initial, ChipKind.Nes, patch).Parameters.Tone.Envelope.Volume);
                return;
            }
            SfxParameterException exception = Assert.Throws<SfxParameterException>(() => SfxParameterPatch.Apply(initial, ChipKind.Nes, patch));
            Assert.Equal("InvalidParameter", exception.Code);
            Assert.Equal("tone.envelope.volume", exception.ParameterPath);
        }

        /// <summary>実数のアンダーフローで正の repeat を無効化したり、負の包絡時間をゼロに救済しない。</summary>
        [Theory]
        [InlineData("tone.repeatPeriodSeconds", "1e-400")]
        [InlineData("tone.envelope.attackSeconds", "-1e-400")]
        public void Apply_DoesNotHideOutOfRangeUnderflow(string path, string token)
        {
            SfxParameters initial = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            string patch = SfxParameterTestJson.Patch(path, "number").Replace("\"number\"", token, StringComparison.Ordinal);
            SfxParameterException exception = Assert.Throws<SfxParameterException>(() => SfxParameterPatch.Apply(initial, ChipKind.Nes, patch));
            Assert.Equal("InvalidParameter", exception.Code);
            Assert.Equal(path, exception.ParameterPath);
        }

        /// <summary>カタログの全離散値を受理し、型への対応を独立した JSON 読み取りで確認する。</summary>
        [Fact]
        public void Apply_AcceptsEveryDiscreteChoice()
        {
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll())
            {
                foreach (object choice in description.Choices)
                {
                    SfxParameters initial = SfxParameterCatalog.CreateDefaults(description.Chip);
                    SfxParameterPatchResult result = SfxParameterPatch.Apply(initial, description.Chip,
                        SfxParameterTestJson.Patch(description.Path, choice));
                    JsonElement actual = SfxParameterTestJson.Read(result.Parameters, description.Path);
                    Assert.Equal(JsonSerializer.Serialize(choice), actual.GetRawText());
                }
            }
        }
    }
}
