using System;

namespace Arpeggio.Formats.Export
{
    /// <summary>絶対サンプルと NTSC PLAY 時刻の整数変換。</summary>
    public static class NsfTiming
    {
        /// <summary>NTSC の PLAY 呼び出し間隔。</summary>
        public const int PlayMicroseconds = 16639;
        internal const long MicrosecondsPerSecond = 1000000;
        private const long FrameDenominator = (long)ConversionLimits.ControlSampleRate * PlayMicroseconds;

        /// <summary>非負の絶対サンプルを中間値切り上げで PLAY フレームへ変換する。</summary>
        public static long Quantize(long positionSamples)
        {
            if (positionSamples < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionSamples));
            }
            return checked((positionSamples * MicrosecondsPerSecond + FrameDenominator / 2) / FrameDenominator);
        }

        internal static long RepresentativeSamples(long frame)
            => checked((frame * FrameDenominator + MicrosecondsPerSecond / 2) / MicrosecondsPerSecond);

        internal static double ErrorMicroseconds(long positionSamples, long frame)
            => Math.Abs(checked(frame * FrameDenominator - positionSamples * MicrosecondsPerSecond)) /
                (double)ConversionLimits.ControlSampleRate;
    }
}
