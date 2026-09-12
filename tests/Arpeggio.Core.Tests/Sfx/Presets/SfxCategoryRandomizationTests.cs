using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Presets
{
    /// <summary>カテゴリの完全初期値を基点とする抽選順と性格、anyの追加消費を固定する。</summary>
    public sealed class SfxCategoryRandomizationTests
    {
        /// <summary>独立計算したseed1の全トーン抽選値と三チップの組み合わせ。</summary>
        public static IEnumerable<object[]> GoldenCases()
        {
            object[][] presets =
            {
                new object[] { SfxPresetKind.Jump, 138.598977, 0.018607, 0.076216, 135.486510, 0.0 },
                new object[] { SfxPresetKind.Coin, 370.010568, 0.055820, 0.057162, 0.0, 0.0 },
                new object[] { SfxPresetKind.Hit, 138.598977, 0.0, 0.085743, -84.679069, 0.0 },
                new object[] { SfxPresetKind.Explosion, 440.0, 0.05, 0.15, 0.0, 0.0 },
                new object[] { SfxPresetKind.PowerUp, 138.598977, 0.111640, 0.171486, 25.403721, 32.331562 },
                new object[] { SfxPresetKind.Laser, 622.281119, 0.0, 0.085743, -254.037206, 0.0 },
                new object[] { SfxPresetKind.Blip, 370.010568, 0.018607, 0.016667, 0.0, 0.0 },
                new object[] { SfxPresetKind.Select, 370.010568, 0.055820, 0.028581, 0.0, 0.0 }
            };
            foreach (ChipKind chip in new[] { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes })
            {
                foreach (object[] preset in presets)
                {
                    var row = new object[preset.Length + 1];
                    row[0] = chip;
                    preset.CopyTo(row, 1);
                    yield return row;
                }
            }
        }

        /// <summary>全十五抽選の適用先と未変更項目を、完全パラメータの値比較で検証する。</summary>
        [Theory]
        [MemberData(nameof(GoldenCases))]
        public void Randomize_SeedOneMatchesCompleteGolden(ChipKind chip, SfxPresetKind kind,
            double frequency, double sustain, double decay, double slide, double acceleration)
        {
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            SfxParameters original = SfxParameterCatalog.CreateDefaults(chip);
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Randomize(original, chip, preset.Name, 1);
            SfxParameters expected = preset.Parameters;
            if (expected.Tone.Enabled)
            {
                expected = expected with
                {
                    Tone = expected.Tone with
                    {
                        BaseFrequencyHz = frequency, SlideSemitonesPerSecond = slide,
                        DeltaSlideSemitonesPerSecondSquared = acceleration, VibratoDepthCents = 7,
                        VibratoSpeedHz = 3.608744,
                        Envelope = expected.Tone.Envelope with { SustainSeconds = sustain, DecaySeconds = decay, Volume = 11 }
                    }
                };
            }
            expected = ExpectedNoiseAndDuty(expected, chip, kind);
            Assert.True(result.Changed);
            Assert.Equal(expected, result.Parameters);
            Assert.Equal(preset.Name, result.SourcePreset);
            Assert.Equal(new SfxRandomization
            {
                Operation = SfxRandomizationOperation.Randomize, Seed = 1, Category = preset.Name
            }, result.Randomization);
            SongValidator.Validate(SfxSongCompiler.Compile(result.Parameters, chip).Song);
            Assert.Equal(SfxParameterCatalog.CreateDefaults(chip), original);
        }

        /// <summary>同じ要求は現在値に依存せず、別名は正式カテゴリで保存する。</summary>
        [Theory]
        [InlineData("pickup", "coin")]
        [InlineData("POWER-UP", "powerup")]
        public void Randomize_AliasesAndCurrentValuesDoNotAffectRecipe(string alias, string canonical)
        {
            SfxParameters first = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameters second = SfxParameterPresetCatalog.Get(SfxPresetKind.Hit, ChipKind.Nes).Parameters;
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Randomize(first, ChipKind.Nes, alias, 0);
            Assert.Equal(SfxParameterRandomizer.Randomize(second, ChipKind.Nes, canonical, 0).Parameters, result.Parameters);
            Assert.Equal(canonical, result.Randomization!.Category);
            Assert.Equal(canonical, result.SourcePreset);
        }

        /// <summary>anyは一回余分に抽選し、要求anyと選ばれた正式名を別々に保存する。</summary>
        [Theory]
        [InlineData(1u, "jump", 140.113997)]
        [InlineData(0u, "hit", 205.719506)]
        [InlineData(uint.MaxValue, "jump", 274.223195)]
        public void Randomize_AnyConsumesCategoryDraw(uint seed, string selected, double frequency)
        {
            SfxParameters original = SfxParameterCatalog.CreateDefaults(ChipKind.Nes);
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Randomize(original, ChipKind.Nes, " ANY ", seed);
            Assert.Equal("any", result.Randomization!.Category);
            Assert.Equal(selected, result.SourcePreset);
            Assert.Equal(frequency, result.Parameters.Tone.BaseFrequencyHz);
            Assert.NotEqual(SfxParameterRandomizer.Randomize(original, ChipKind.Nes, selected, seed).Parameters, result.Parameters);
        }

        /// <summary>anyの八区間は設計の表示順と一致する。</summary>
        [Theory]
        [InlineData(1u, "jump")]
        [InlineData(2048u, "coin")]
        [InlineData(4096u, "hit")]
        [InlineData(6144u, "explosion")]
        [InlineData(8192u, "powerup")]
        [InlineData(10240u, "laser")]
        [InlineData(12288u, "blip")]
        [InlineData(14336u, "select")]
        public void Randomize_AnyUsesFixedDisplayOrder(uint seed, string selected)
        {
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Randomize(
                SfxParameterCatalog.CreateDefaults(ChipKind.Nes), ChipKind.Nes, "any", seed);
            Assert.Equal(selected, result.SourcePreset);
        }

        /// <summary>seed端点でもレイヤー・ジャンプ・符号・波形など用途の性格を維持する。</summary>
        [Theory]
        [MemberData(nameof(SfxParameterPresetCatalogTests.Cases), MemberType = typeof(SfxParameterPresetCatalogTests))]
        public void Randomize_PreservesCategoryCharacter(ChipKind chip, SfxPresetKind kind)
        {
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            SfxParameters original = preset.Parameters;
            foreach (uint seed in new[] { 0u, 1u, uint.MaxValue })
            {
                SfxParameters candidate = SfxParameterRandomizer.Randomize(original, chip, preset.Name, seed).Parameters;
                Assert.Equal(original.Tone.Enabled, candidate.Tone.Enabled);
                Assert.Equal(original.Noise.Enabled, candidate.Noise.Enabled);
                Assert.Equal(original.Tone.PitchChangeSemitones, candidate.Tone.PitchChangeSemitones);
                Assert.Equal(original.Tone.PitchChangeTimeSeconds, candidate.Tone.PitchChangeTimeSeconds);
                Assert.Equal(original.Tone.RepeatPeriodSeconds, candidate.Tone.RepeatPeriodSeconds);
                Assert.Equal(original.Tone.Envelope.Punch, candidate.Tone.Envelope.Punch);
                Assert.Equal(Math.Sign(original.Tone.SlideSemitonesPerSecond), Math.Sign(candidate.Tone.SlideSemitonesPerSecond));
                Assert.Equal(Math.Sign(original.Tone.DeltaSlideSemitonesPerSecondSquared), Math.Sign(candidate.Tone.DeltaSlideSemitonesPerSecondSquared));
                Assert.Equal(original.Nes?.NoiseMode, candidate.Nes?.NoiseMode);
                Assert.Equal(original.GameBoy?.NoiseWidth, candidate.GameBoy?.NoiseWidth);
                Assert.Equal(original.Snes?.Waveform, candidate.Snes?.Waveform);
                if (!original.Tone.Enabled)
                {
                    Assert.Equal(original.Tone, candidate.Tone);
                }
                if (!original.Noise.Enabled)
                {
                    Assert.Equal(original.Noise, candidate.Noise);
                }
                SongValidator.Validate(SfxSongCompiler.Compile(candidate, chip).Song);
            }
        }

        /// <summary>同じカテゴリとseedを再適用した同値結果は出自更新を要求しない。</summary>
        [Fact]
        public void Randomize_SameValuesAreNoOp()
        {
            SfxParameters original = SfxParameterCatalog.CreateDefaults(ChipKind.Snes);
            SfxParameterRandomizationResult first = SfxParameterRandomizer.Randomize(original, ChipKind.Snes, "any", 0);
            SfxParameterRandomizationResult second = SfxParameterRandomizer.Randomize(first.Parameters, ChipKind.Snes, "any", 0);
            Assert.False(second.Changed);
            Assert.Same(first.Parameters, second.Parameters);
            Assert.Null(second.Randomization);
            Assert.Null(second.SourcePreset);
            Assert.Empty(second.Changes);
        }

        private static SfxParameters ExpectedNoiseAndDuty(SfxParameters expected, ChipKind chip, SfxPresetKind kind)
        {
            if (expected.Noise.Enabled)
            {
                bool isHit = kind == SfxPresetKind.Hit;
                expected = expected with { Noise = expected.Noise with { Envelope = expected.Noise.Envelope with
                {
                    SustainSeconds = isHit ? 0 : 0.018645, DecaySeconds = isHit ? 0.056540 : 0.325102, Volume = 12
                } } };
            }
            double duty = expected.Tone.Enabled ? 12.5 : 25;
            return chip switch
            {
                ChipKind.Nes => expected with { Nes = expected.Nes! with
                {
                    DutyPercent = duty, NoisePeriodIndex = expected.Noise.Enabled ? expected.Nes!.NoisePeriodIndex - 3 : 12
                } },
                ChipKind.GameBoy => expected with { GameBoy = expected.GameBoy! with
                {
                    DutyPercent = duty, NoiseSelection = expected.Noise.Enabled ? expected.GameBoy!.NoiseSelection - 8 : 96
                } },
                _ => expected with { Snes = expected.Snes! with
                {
                    NoiseRate = expected.Noise.Enabled ? expected.Snes!.NoiseRate - 3 : 24
                } }
            };
        }
    }
}
