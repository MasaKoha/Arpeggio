using System;
using Arpeggio.Core.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>dBFS の基準と無音表現を固定する。</summary>
    public sealed class DecibelScaleTests
    {
        /// <summary>フルスケールと既知の十分の一振幅を相互変換する。</summary>
        [Theory]
        [InlineData(1, 0)]
        [InlineData(0.1, -20)]
        [InlineData(0.001, -60)]
        [InlineData(10, 20)]
        public void ConvertsKnownAmplitudes(double amplitude, double decibels)
        {
            Assert.Equal(decibels, DecibelScale.ToDecibels(amplitude), 10);
            Assert.Equal(amplitude, DecibelScale.ToLinear(decibels), 10);
        }

        /// <summary>ゼロと極小入力を有限の下限にそろえ、逆変換はゼロにする。</summary>
        [Fact]
        public void SilenceUsesFiniteFloor()
        {
            Assert.Equal(-160, DecibelScale.ToDecibels(0));
            Assert.Equal(-160, DecibelScale.ToDecibels(double.Epsilon));
            Assert.Equal(0, DecibelScale.ToLinear(-160));
            Assert.Throws<ArgumentOutOfRangeException>(() => DecibelScale.ToDecibels(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => DecibelScale.ToLinear(double.NaN));
        }
    }
}
