using System;
using System.Numerics;
using Arpeggio.Core.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>既知の離散変換と独立した直接和で基数 2 の変換を検証する。</summary>
    public sealed class FastFourierTransformTests
    {
        private const int TransformSize = 8;
        private const double Tolerance = 0.000000001;

        /// <summary>単位インパルスの全ビンが 1 になる。</summary>
        [Fact]
        public void Transform_ImpulseHasFlatSpectrum()
        {
            Complex[] spectrum = new Complex[TransformSize];
            spectrum[0] = Complex.One;
            FastFourierTransform.Transform(spectrum);
            Assert.All(spectrum, value => Assert.InRange(Complex.Abs(value - Complex.One), 0, Tolerance));
        }

        /// <summary>定数入力は DC ビンだけに集まる。</summary>
        [Fact]
        public void Transform_ConstantIsOnlyDc()
        {
            Complex[] spectrum = new Complex[TransformSize];
            Array.Fill(spectrum, Complex.One);
            FastFourierTransform.Transform(spectrum);
            Assert.Equal(new Complex(TransformSize, 0), spectrum[0]);
            for (int index = 1; index < spectrum.Length; index++)
            {
                Assert.InRange(spectrum[index].Magnitude, 0, Tolerance);
            }
        }

        /// <summary>実部・虚部を持つ入力が、符号を含めて直接計算の DFT と一致する。</summary>
        [Fact]
        public void Transform_MatchesIndependentDiscreteSum()
        {
            Complex[] input = { new Complex(1, 2), new Complex(-3, 1), new Complex(2, -4), Complex.Zero,
                new Complex(5, 1), new Complex(-1, -1), new Complex(2, 3), new Complex(0, -2) };
            Complex[] actual = (Complex[])input.Clone();
            FastFourierTransform.Transform(actual);
            for (int frequency = 0; frequency < input.Length; frequency++)
            {
                Complex expected = Complex.Zero;
                for (int sample = 0; sample < input.Length; sample++)
                {
                    expected += input[sample] * Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency * sample / input.Length);
                }
                Assert.InRange(Complex.Abs(expected - actual[frequency]), 0, Tolerance);
            }
        }

        /// <summary>変換点数の前提に合わない入力を拒否する。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(1000)]
        public void Transform_RejectsInvalidSizes(int size)
        {
            Assert.Throws<ArgumentException>(() => FastFourierTransform.Transform(new Complex[size]));
        }
    }
}
