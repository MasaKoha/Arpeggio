using System;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>波形の音程、デューティ、実効値を独立した数値計算で検証する。</summary>
    internal static class SignalAnalysis
    {
        internal const double FrequencyRelativeTolerance = 0.02;
        internal const double DutyTolerance = 0.03;
        internal const double SilenceTolerance = 0.000001;

        internal static double EstimateFrequency(ReadOnlySpan<float> samples, int sampleRate)
        {
            double midpoint = GetMidpoint(samples);
            int firstCrossing = -1;
            int lastCrossing = -1;
            int crossingCount = 0;
            for (int index = 1; index < samples.Length; index++)
            {
                if (samples[index - 1] > midpoint || samples[index] <= midpoint)
                {
                    continue;
                }

                if (firstCrossing < 0)
                {
                    firstCrossing = index;
                }

                lastCrossing = index;
                crossingCount++;
            }

            // 観測窓の端にある半端な周期を除き、短い波形でも周期数の丸め誤差を避ける。
            return crossingCount < 2 ? 0 : (crossingCount - 1) * (double)sampleRate / (lastCrossing - firstCrossing);
        }

        internal static double HighRatio(ReadOnlySpan<float> samples)
        {
            double midpoint = GetMidpoint(samples);
            int highCount = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                if (samples[index] > midpoint + SilenceTolerance)
                {
                    highCount++;
                }
            }

            return highCount / (double)samples.Length;
        }

        internal static double RootMeanSquare(ReadOnlySpan<float> samples)
        {
            double sumSquares = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                sumSquares += (double)samples[index] * samples[index];
            }

            return samples.Length == 0 ? 0 : Math.Sqrt(sumSquares / samples.Length);
        }

        internal static double ExpectedFrequency(int midiNote)
        {
            const int ReferenceMidiNote = 69;
            const double ReferenceFrequency = 440;
            const double SemitonesPerOctave = 12;
            return ReferenceFrequency * Math.Pow(2, (midiNote - ReferenceMidiNote) / SemitonesPerOctave);
        }

        private static double GetMidpoint(ReadOnlySpan<float> samples)
        {
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            for (int index = 0; index < samples.Length; index++)
            {
                minimum = Math.Min(minimum, samples[index]);
                maximum = Math.Max(maximum, samples[index]);
            }

            // NES の非線形ミキサー向け単極波形と双極波形を同じ基準で解析する。
            return (minimum + maximum) / 2;
        }
    }
}
