using System;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Export
{
    /// <summary>DC を除いた周期・振幅を、再合成器の内部状態を使わず測定する。</summary>
    internal static class RegisterTraceAssertions
    {
        internal static double AlternatingRootMeanSquare(ReadOnlySpan<double> samples)
        {
            double mean = Mean(samples);
            double energy = 0;
            foreach (double sample in samples)
            {
                double centered = sample - mean;
                energy += centered * centered;
            }
            return Math.Sqrt(energy / samples.Length);
        }

        internal static void HasFrequency(ReadOnlySpan<double> samples, double expectedFrequency)
        {
            const double tolerance = 0.02;
            double mean = Mean(samples);
            int firstCrossing = -1;
            int lastCrossing = -1;
            int crossingCount = 0;
            for (int sample = 1; sample < samples.Length; sample++)
            {
                if (samples[sample - 1] <= mean && samples[sample] > mean)
                {
                    if (firstCrossing < 0)
                    {
                        firstCrossing = sample;
                    }
                    lastCrossing = sample;
                    crossingCount++;
                }
            }
            Assert.True(crossingCount > 2, "周波数測定に必要なゼロクロスがありません。");
            double measured = (crossingCount - 1.0) * RegisterTraceRenderer.SampleRate / (lastCrossing - firstCrossing);
            Assert.InRange(measured, expectedFrequency * (1 - tolerance), expectedFrequency * (1 + tolerance));
        }

        private static double Mean(ReadOnlySpan<double> samples)
        {
            Assert.False(samples.IsEmpty);
            double sum = 0;
            foreach (double sample in samples)
            {
                sum += sample;
            }
            return sum / samples.Length;
        }
    }
}
