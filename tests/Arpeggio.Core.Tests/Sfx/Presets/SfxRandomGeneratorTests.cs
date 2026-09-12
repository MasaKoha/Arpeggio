using System;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Presets
{
    /// <summary>独立した整数期待値で、固定PRNGの版1と全抽選に必要な出力を固定する。</summary>
    public sealed class SfxRandomGeneratorTests
    {
        /// <summary>seed=1の二十二出力はOSやビルドのオーバーフロー設定に依存しない。</summary>
        [Fact]
        public void SeedOne_MatchesAllTwentyTwoOutputs()
        {
            uint[] expected =
            {
                270369, 67634689, 2647435461, 307599695, 2398689233, 745495504,
                632435482, 435756210, 2005365029, 2916098932, 2657092299, 1495045943,
                3031976842, 82049198, 87470069, 3385103793, 891394312, 3323190024,
                321008529, 4283899417, 2383559219, 3822316985
            };
            AssertOutputs(1, expected);
        }

        /// <summary>seed0の置換状態とuint最大値にも固定期待値を持つ。</summary>
        [Theory]
        [InlineData(0u, 1085196063u, 2447379481u, 2618286376u, 1701901981u, 265159372u)]
        [InlineData(uint.MaxValue, 253983u, 4228382207u, 1958451267u, 4056713434u, 2049502865u)]
        public void EdgeSeeds_MatchGoldenValues(uint seed, uint first, uint second, uint third, uint fourth, uint fifth)
        {
            AssertOutputs(seed, new[] { first, second, third, fourth, fifth });
        }

        /// <summary>0だけが規定の状態へ置換される。</summary>
        [Fact]
        public void ZeroSeed_UsesSpecifiedNonzeroState()
        {
            var zero = new SfxRandomGenerator(0);
            var replacement = new SfxRandomGenerator(0x6D2B79F5);
            for (int draw = 0; draw < 64; draw++)
            {
                Assert.Equal(replacement.NextUInt32(), zero.NextUInt32());
            }
        }

        private static void AssertOutputs(uint seed, uint[] expected)
        {
            var integers = new SfxRandomGenerator(seed);
            var fractions = new SfxRandomGenerator(seed);
            foreach (uint value in expected)
            {
                Assert.Equal(value, integers.NextUInt32());
                double fraction = fractions.NextDouble();
                Assert.Equal(value / 4294967296.0, fraction);
                Assert.InRange(fraction, 0, Math.BitDecrement(1.0));
            }
        }
    }
}
