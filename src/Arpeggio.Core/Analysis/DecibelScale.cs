using System;

namespace Arpeggio.Core.Analysis
{
    /// <summary>振幅と有限の dBFS を相互変換する純関数。</summary>
    public static class DecibelScale
    {
        /// <summary>無音の表現下限。JSON の非有限数を避ける。</summary>
        public const double MinimumDecibels = -160;
        private const double AmplitudeLogarithmScale = 20;

        /// <summary>非負の線形振幅を dBFS に変換する。</summary>
        public static double ToDecibels(double amplitude)
        {
            if (!double.IsFinite(amplitude) || amplitude < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amplitude));
            }
            return amplitude == 0 ? MinimumDecibels
                : Math.Max(MinimumDecibels, AmplitudeLogarithmScale * Math.Log10(amplitude));
        }

        /// <summary>dBFS を線形振幅へ変換する。下限以下はゼロ。</summary>
        public static double ToLinear(double decibels)
        {
            if (!double.IsFinite(decibels))
            {
                throw new ArgumentOutOfRangeException(nameof(decibels));
            }
            return decibels <= MinimumDecibels ? 0 : Math.Pow(10, decibels / AmplitudeLogarithmScale);
        }
    }
}
