using System;

namespace Arpeggio.Core.Analysis
{
    /// <summary>スペクトルとは独立した左右サンプルの統計。</summary>
    internal readonly record struct SignalStatistics
    {
        internal double LeftPower { get; init; }
        internal double RightPower { get; init; }
        internal double Peak { get; init; }
        internal long ClippedSamples { get; init; }
        internal double Rms => Math.Sqrt((LeftPower + RightPower) / 2);

        internal static SignalStatistics Measure(ReadOnlySpan<float> samples)
        {
            double leftSum = 0;
            double rightSum = 0;
            double peak = 0;
            long clipped = 0;
            for (int index = 0; index < samples.Length; index += 2)
            {
                double left = samples[index];
                double right = samples[index + 1];
                if (!double.IsFinite(left) || !double.IsFinite(right))
                {
                    throw new ArgumentException("音声サンプルは有限値で指定してください。", nameof(samples));
                }
                leftSum += left * left;
                rightSum += right * right;
                peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
                clipped += Math.Abs(left) >= 1 ? 1 : 0;
                clipped += Math.Abs(right) >= 1 ? 1 : 0;
            }
            int frames = samples.Length / 2;
            return new SignalStatistics
            {
                LeftPower = frames == 0 ? 0 : leftSum / frames,
                RightPower = frames == 0 ? 0 : rightSum / frames,
                Peak = peak,
                ClippedSamples = clipped
            };
        }
    }
}
