using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>二十二抽選すべての固定値、選択値の丸め、強度の契約を検証する。</summary>
    public sealed class SfxParameterMutationTests
    {
        /// <summary>seed0・1・最大値の独立計算したNES黄金値。</summary>
        public static IEnumerable<object[]> GoldenCases()
        {
            yield return new object[] { 1u, new double[]
            {
                220.019200, -46.110309, 103.873969, 14.323727, 10.584884, 7, 0.129450, 0.240583,
                0.586764, 0.589479, 8, 0.169619, 0.341187, 6, 0.104073, 0.515262,
                0.483018, 0.636870, 25, 34.871153, 8, 19.358861
            } };
            yield return new object[] { 0u, new double[]
            {
                312.279382, 20.378993, 100.616305, 79.250987, 5.617372, 8, 0.286836, 0.393799,
                0.789754, 0.276818, 8, 0.149508, 0.317807, 8, 0.280134, 0.324807,
                0.702409, 0.455768, 75, 11.965770, 8, 1.321996
            } };
            yield return new object[] { uint.MaxValue, new double[]
            {
                220.018036, 70.139634, 26.873985, 188.905440, 9.771871, 11, 0.179401, 0.226059,
                0.679325, 0.447998, 10, 0.274853, 0.287985, 12, 0.220763, 0.247635,
                0.675970, 0.595018, 75, 1.519949, 8, 11.703734
            } };
        }

        /// <summary>全共通項目とNESの四スロットを抽選順の期待値に照合する。</summary>
        [Theory]
        [MemberData(nameof(GoldenCases))]
        public void Mutate_MatchesAllDraws(uint seed, double[] expected)
        {
            string[] paths =
            {
                "tone.baseFrequencyHz", "tone.slideSemitonesPerSecond", "tone.deltaSlideSemitonesPerSecondSquared",
                "tone.vibratoDepthCents", "tone.vibratoSpeedHz", "tone.envelope.volume", "tone.envelope.attackSeconds",
                "tone.envelope.sustainSeconds", "tone.envelope.decaySeconds", "tone.envelope.punch",
                "tone.pitchChangeSemitones", "tone.pitchChangeTimeSeconds", "tone.repeatPeriodSeconds",
                "noise.envelope.volume", "noise.envelope.attackSeconds", "noise.envelope.sustainSeconds",
                "noise.envelope.decaySeconds", "noise.envelope.punch", "nes.dutyPercent", "nes.dutySweepPercentPerSecond",
                "nes.noisePeriodIndex", "nes.noiseSlideIndicesPerSecond"
            };
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, seed, 1);
            for (int index = 0; index < paths.Length; index++)
            {
                Assert.Equal(expected[index], SfxParameterTestJson.Read(result.Parameters, paths[index]).GetDouble());
            }
            Assert.True(result.Parameters.Tone.Enabled);
            Assert.True(result.Parameters.Noise.Enabled);
            Assert.Equal(original.Nes!.NoiseMode, result.Parameters.Nes!.NoiseMode);
            Assert.Equal(SfxRandomizationOperation.Mutate, result.Randomization!.Operation);
            Assert.Equal(1, result.Randomization.AlgorithmVersion);
            Assert.Equal(seed, result.Randomization.Seed);
            Assert.Equal(1.0, result.Randomization.Strength);
            Assert.Empty(result.Randomization.Locks!);
            Assert.Null(result.Randomization.Category);
            Assert.Null(result.SourcePreset);
            Assert.Equal(SfxHash.ComputeParametersHash(original, ChipKind.Nes), result.Randomization.BaseParametersHash);
            Assert.Equal(SfxRandomizationTestData.RichParameters(ChipKind.Nes), original);
            SongValidator.Validate(SfxSongCompiler.Compile(result.Parameters, ChipKind.Nes).Song);
        }

        /// <summary>GBの選択値幅と、SNESで非対応二項目を消費した後のrate抽選を固定する。</summary>
        [Theory]
        [InlineData(1u, 66, 47.435442, 16)]
        [InlineData(0u, 66, -24.712016, 16)]
        [InlineData(uint.MaxValue, 63, 16.814934, 16)]
        public void Mutate_UsesChipSpecificWidthsAndSlots(uint seed, int selection, double slide, int rate)
        {
            SfxParameters gameBoyOriginal = SfxRandomizationTestData.RichParameters(ChipKind.GameBoy);
            SfxParameters snesOriginal = SfxRandomizationTestData.RichParameters(ChipKind.Snes);
            SfxParameters gameBoy = SfxParameterRandomizer.Mutate(gameBoyOriginal, ChipKind.GameBoy, seed, 1).Parameters;
            SfxParameters snes = SfxParameterRandomizer.Mutate(snesOriginal, ChipKind.Snes, seed, 1).Parameters;
            SfxParameters nes = SfxParameterRandomizer.Mutate(SfxRandomizationTestData.RichParameters(ChipKind.Nes), ChipKind.Nes, seed, 1).Parameters;
            Assert.Equal(selection, gameBoy.GameBoy!.NoiseSelection);
            Assert.Equal(slide, gameBoy.GameBoy.NoiseSlideSelectionsPerSecond);
            Assert.Equal(nes.Nes!.DutyPercent, gameBoy.GameBoy.DutyPercent);
            Assert.Equal(nes.Nes.DutySweepPercentPerSecond, gameBoy.GameBoy.DutySweepPercentPerSecond);
            Assert.Equal(gameBoyOriginal.GameBoy!.NoiseWidth, gameBoy.GameBoy.NoiseWidth);
            Assert.Equal(rate, snes.Snes!.NoiseRate);
            Assert.Equal(snesOriginal.Snes!.Waveform, snes.Snes.Waveform);
            Assert.Equal(nes.Tone, gameBoy.Tone);
            Assert.Equal(nes.Noise, gameBoy.Noise);
            Assert.Equal(nes.Tone, snes.Tone);
            Assert.Equal(nes.Noise, snes.Noise);
        }

        /// <summary>周波数以外の実数は強度に比例し、既定強度も0.1を使う。</summary>
        [Fact]
        public void Mutate_StrengthScalesSymmetricOffsets()
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1);
            Assert.Equal(SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 0.1).Parameters, result.Parameters);
            Assert.Equal(6.188969, result.Parameters.Tone.SlideSemitonesPerSecond);
            Assert.Equal(0.1, result.Randomization!.Strength);
        }

        /// <summary>dutyの正確な中間点は小さい段階へ量子化する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        public void Mutate_DutyMidpointSelectsLowerChoice(ChipKind chip)
        {
            // このseedの十九番目の出力は1073741824であり、50から正確に12.5を引く。
            const uint MidpointSeed = 2024552376;
            SfxParameters original = SfxRandomizationTestData.RichParameters(chip);
            SfxParameters result = SfxParameterRandomizer.Mutate(original, chip, MidpointSeed, 1).Parameters;
            double actual = chip == ChipKind.Nes ? result.Nes!.DutyPercent : result.GameBoy!.DutyPercent;
            Assert.Equal(25, actual);
        }
    }
}
