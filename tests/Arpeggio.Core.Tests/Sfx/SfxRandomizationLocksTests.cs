using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>ロックと無効レイヤーによる抽選位置不変、補正優先順位、同値操作を検証する。</summary>
    public sealed class SfxRandomizationLocksTests
    {
        /// <summary>全正規パスを単独でロックするケース。</summary>
        public static IEnumerable<object[]> LockCases()
        {
            foreach (ChipKind chip in new[] { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes })
            {
                foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll(chip))
                {
                    yield return new object[] { chip, description.Path };
                }
            }
        }

        /// <summary>一項目のロックで後続の全抽選がずれず、非数値の正規パスも受理する。</summary>
        [Theory]
        [MemberData(nameof(LockCases))]
        public void Mutate_EachLockPreservesOnlyThatValue(ChipKind chip, string lockedPath)
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(chip);
            SfxParameters unlocked = SfxParameterRandomizer.Mutate(original, chip, 1, 1).Parameters;
            SfxParameterRandomizationResult locked = SfxParameterRandomizer.Mutate(original, chip, 1, 1, new[] { lockedPath });
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll(chip))
            {
                SfxParameters expected = description.Path == lockedPath ? original : unlocked;
                Assert.Equal(SfxParameterTestJson.Read(expected, description.Path).GetRawText(),
                    SfxParameterTestJson.Read(locked.Parameters, description.Path).GetRawText());
            }
            Assert.Equal(new[] { lockedPath }, locked.Randomization!.Locks);
            Assert.DoesNotContain(locked.Changes, change => change.ParameterPath == lockedPath);
        }

        /// <summary>無効なトーンは十三回とduty二回を消費し、ノイズの値には影響しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Mutate_DisabledToneStillConsumesItsDraws(ChipKind chip)
        {
            SfxParameters enabled = SfxRandomizationTestData.RichParameters(chip);
            SfxParameters disabled = enabled with { Tone = enabled.Tone with { Enabled = false } };
            SfxParameters expected = SfxParameterRandomizer.Mutate(enabled, chip, 1, 1).Parameters;
            SfxParameters actual = SfxParameterRandomizer.Mutate(disabled, chip, 1, 1).Parameters;
            Assert.Equal(disabled.Tone, actual.Tone);
            Assert.Equal(expected.Noise, actual.Noise);
            Assert.Equal(expected.Nes?.NoisePeriodIndex, actual.Nes?.NoisePeriodIndex);
            Assert.Equal(expected.Nes?.NoiseSlideIndicesPerSecond, actual.Nes?.NoiseSlideIndicesPerSecond);
            Assert.Equal(expected.GameBoy?.NoiseSelection, actual.GameBoy?.NoiseSelection);
            Assert.Equal(expected.GameBoy?.NoiseSlideSelectionsPerSecond, actual.GameBoy?.NoiseSlideSelectionsPerSecond);
            Assert.Equal(expected.Snes, actual.Snes);
            Assert.Equal(disabled.Nes?.DutyPercent, actual.Nes?.DutyPercent);
            Assert.Equal(disabled.Nes?.DutySweepPercentPerSecond, actual.Nes?.DutySweepPercentPerSecond);
            Assert.Equal(disabled.GameBoy?.DutyPercent, actual.GameBoy?.DutyPercent);
            Assert.Equal(disabled.GameBoy?.DutySweepPercentPerSecond, actual.GameBoy?.DutySweepPercentPerSecond);
        }

        /// <summary>無効ノイズでも五回の抽選を消費し、後続dutyの結果を維持する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Mutate_DisabledNoiseStillConsumesItsDraws(ChipKind chip)
        {
            SfxParameters enabled = SfxRandomizationTestData.RichParameters(chip);
            SfxParameters disabled = enabled with { Noise = enabled.Noise with { Enabled = false } };
            SfxParameters expected = SfxParameterRandomizer.Mutate(enabled, chip, 1, 1).Parameters;
            SfxParameters actual = SfxParameterRandomizer.Mutate(disabled, chip, 1, 1).Parameters;
            Assert.Equal(disabled.Noise, actual.Noise);
            Assert.Equal(expected.Tone, actual.Tone);
            Assert.Equal(expected.Nes?.DutyPercent, actual.Nes?.DutyPercent);
            Assert.Equal(expected.Nes?.DutySweepPercentPerSecond, actual.Nes?.DutySweepPercentPerSecond);
            Assert.Equal(expected.GameBoy?.DutyPercent, actual.GameBoy?.DutyPercent);
            Assert.Equal(expected.GameBoy?.DutySweepPercentPerSecond, actual.GameBoy?.DutySweepPercentPerSecond);
            Assert.Equal(disabled.Nes?.NoisePeriodIndex, actual.Nes?.NoisePeriodIndex);
            Assert.Equal(disabled.Nes?.NoiseSlideIndicesPerSecond, actual.Nes?.NoiseSlideIndicesPerSecond);
            Assert.Equal(disabled.GameBoy?.NoiseSelection, actual.GameBoy?.NoiseSelection);
            Assert.Equal(disabled.GameBoy?.NoiseSlideSelectionsPerSecond, actual.GameBoy?.NoiseSlideSelectionsPerSecond);
            Assert.Equal(disabled.Snes, actual.Snes);
        }

        /// <summary>punch未ロックならpunchを0へ戻し、補正前の値も変更一覧へ残す。</summary>
        [Fact]
        public void Mutate_UnlockedPunchIsClearedWhenSustainBecomesZero()
        {
            SfxParameters original = SfxParameterPresetCatalog.Get(SfxPresetKind.Coin, ChipKind.Nes).Parameters;
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 1);
            Assert.Equal(0, result.Parameters.Tone.Envelope.SustainSeconds);
            Assert.Equal(0, result.Parameters.Tone.Envelope.Punch);
            SfxParameterChange correction = Assert.Single(result.Changes, change => change.CorrectedFrom.HasValue);
            Assert.Equal("tone.envelope.punch", correction.ParameterPath);
            Assert.Equal(0.25, Assert.IsType<double>(correction.PreviousValue));
            Assert.Equal(0.339479, correction.CorrectedFrom);
            Assert.Equal(0.0, Assert.IsType<double>(correction.Value));
        }

        /// <summary>punchをロックした場合は未ロックsustainだけを一フレームへ戻す。</summary>
        [Fact]
        public void Mutate_LockedPunchRepairsUnlockedSustain()
        {
            SfxParameters original = SfxParameterPresetCatalog.Get(SfxPresetKind.Coin, ChipKind.Nes).Parameters;
            string[] locks = { "tone.envelope.punch" };
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 1, locks);
            Assert.Equal(0.016667, result.Parameters.Tone.Envelope.SustainSeconds);
            Assert.Equal(0.25, result.Parameters.Tone.Envelope.Punch);
            SfxParameterChange correction = Assert.Single(result.Changes, change => change.CorrectedFrom.HasValue);
            Assert.Equal("tone.envelope.sustainSeconds", correction.ParameterPath);
            Assert.Equal(0.0, correction.CorrectedFrom);
            Assert.DoesNotContain(result.Changes, change => change.ParameterPath == locks[0]);
        }

        /// <summary>両方のロックは元の有効値を保持し、sustain0だけのロックではpunchを戻す。</summary>
        [Fact]
        public void Mutate_EnvelopeLocksNeverChangeLockedValues()
        {
            SfxParameters original = SfxParameterPresetCatalog.Get(SfxPresetKind.Coin, ChipKind.Nes).Parameters;
            string[] locks = { "tone.envelope.punch", "tone.envelope.sustainSeconds" };
            SfxParameterRandomizationResult both = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 1, locks);
            Assert.Equal(original.Tone.Envelope.Punch, both.Parameters.Tone.Envelope.Punch);
            Assert.Equal(original.Tone.Envelope.SustainSeconds, both.Parameters.Tone.Envelope.SustainSeconds);
            Assert.DoesNotContain(both.Changes, change => change.CorrectedFrom.HasValue);
            SfxParameters zero = original with { Tone = original.Tone with { Envelope = original.Tone.Envelope with
            {
                SustainSeconds = 0, Punch = 0
            } } };
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(zero, ChipKind.Nes, 1, 1,
                new[] { "tone.envelope.sustainSeconds" });
            Assert.Equal(0, result.Parameters.Tone.Envelope.SustainSeconds);
            Assert.Equal(0, result.Parameters.Tone.Envelope.Punch);
            Assert.Contains(result.Changes, change => change.ParameterPath == "tone.envelope.punch"
                && change.CorrectedFrom == 0.089479);
        }

        /// <summary>トーンとノイズの補正は独立し、両方のロック済みsustainを維持する。</summary>
        [Fact]
        public void Mutate_RepairsBothEnvelopesIndependently()
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            var zeroSustain = new SfxEnvelopeParameters { SustainSeconds = 0, Punch = 0 };
            original = original with
            {
                Tone = original.Tone with { Envelope = zeroSustain },
                Noise = original.Noise with { Envelope = zeroSustain }
            };
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 1,
                new[] { "tone.envelope.sustainSeconds", "noise.envelope.sustainSeconds" });
            Assert.Equal(0, result.Parameters.Tone.Envelope.SustainSeconds);
            Assert.Equal(0, result.Parameters.Noise.Envelope.SustainSeconds);
            Assert.Equal(0, result.Parameters.Tone.Envelope.Punch);
            Assert.Equal(0, result.Parameters.Noise.Envelope.Punch);
            SfxParameterChange toneCorrection = Assert.Single(result.Changes,
                change => change.ParameterPath == "tone.envelope.punch");
            SfxParameterChange noiseCorrection = Assert.Single(result.Changes,
                change => change.ParameterPath == "noise.envelope.punch");
            Assert.Equal(0.089479, toneCorrection.CorrectedFrom);
            Assert.Equal(0.136870, noiseCorrection.CorrectedFrom);
        }

        /// <summary>強度0・丸め後同値・全ロックはパラメータ参照と出自を更新しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Mutate_NoOpDoesNotRequestProvenanceUpdate(ChipKind chip)
        {
            SfxParameters original = SfxRandomizationTestData.RichParameters(chip);
            string[] allLocks = SfxParameterCatalog.GetAll(chip).Select(description => description.Path).ToArray();
            SfxParameterRandomizationResult[] results =
            {
                SfxParameterRandomizer.Mutate(original, chip, 1, 0),
                SfxParameterRandomizer.Mutate(original, chip, 1, 1e-14),
                SfxParameterRandomizer.Mutate(original, chip, 1, 1, allLocks)
            };
            foreach (SfxParameterRandomizationResult result in results)
            {
                Assert.False(result.Changed);
                Assert.Same(original, result.Parameters);
                Assert.Null(result.Randomization);
                Assert.Null(result.SourcePreset);
                Assert.Empty(result.Changes);
            }
        }

        /// <summary>ロック配列を独立保持し、重複は先頭順で除く。</summary>
        [Fact]
        public void Mutate_LocksAreCopiedAndDeduplicated()
        {
            string[] locks = { "tone.envelope.volume", "tone.baseFrequencyHz", "tone.envelope.volume" };
            SfxParameters original = SfxRandomizationTestData.RichParameters(ChipKind.Nes);
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Mutate(original, ChipKind.Nes, 1, 1, locks);
            locks[0] = "noise.enabled";
            Assert.Equal(new[] { "tone.envelope.volume", "tone.baseFrequencyHz" }, result.Randomization!.Locks);
            IList<string> savedLocks = Assert.IsAssignableFrom<IList<string>>(result.Randomization.Locks);
            Assert.Throws<NotSupportedException>(() => savedLocks[0] = "noise.enabled");
        }
    }
}
