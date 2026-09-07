using System;
using System.Numerics;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>ガウス係数の直流利得と、線形補間に対する高域イメージ抑制。</summary>
    public sealed class GaussianInterpolatorTests
    {
        /// <summary>全小数位相で四係数の合計がほぼ一定になる。</summary>
        [Fact]
        public void Weights_HaveNearlyConstantSum()
        {
            for (int fraction = 0; fraction < 256; fraction++)
            {
                GaussianInterpolator.GetWeights(fraction / 256.0, out double first, out double second, out double third, out double fourth);
                Assert.InRange(first + second + third + fourth, 0.998, 1.002);
                Assert.InRange(first, 0, 1);
                Assert.InRange(second, 0, 1);
                Assert.InRange(third, 0, 1);
                Assert.InRange(fourth, 0, 1);
            }
        }

        /// <summary>20 kHz 素材の 8 kHz 正弦波を 32 kHz へ再生し、12 kHz 以上のイメージを比較する。</summary>
        [Fact]
        public void Interpolate_ReducesHighFrequencyImagesComparedWithLinear()
        {
            const int SampleCount = 8192;
            const double SourceStep = 20000.0 / 32000;
            Complex[] linear = new Complex[SampleCount];
            Complex[] gaussian = new Complex[SampleCount];
            for (int index = 0; index < SampleCount; index++)
            {
                double position = index * SourceStep;
                int center = (int)position;
                double fraction = position - center;
                double current = SourceAt(center);
                double next = SourceAt(center + 1);
                linear[index] = new Complex(current + (next - current) * fraction, 0);
                gaussian[index] = new Complex(GaussianInterpolator.Interpolate(SourceAt(center - 1), current, next,
                    SourceAt(center + 2), fraction), 0);
            }
            FastFourierTransform.Transform(linear);
            FastFourierTransform.Transform(gaussian);
            double linearHighPower = HighPower(linear);
            double gaussianHighPower = HighPower(gaussian);
            Assert.True(linearHighPower > 1);
            Assert.True(gaussianHighPower < linearHighPower * 0.8);
        }

        private static double SourceAt(int position) => Math.Sin(2 * Math.PI * 8000 * position / 20000);

        private static double HighPower(Complex[] spectrum)
        {
            double power = 0;
            int firstBin = spectrum.Length * 12000 / 32000;
            for (int index = firstBin; index <= spectrum.Length / 2; index++)
            {
                power += spectrum[index].Magnitude * spectrum[index].Magnitude;
            }
            return power;
        }
    }
}
