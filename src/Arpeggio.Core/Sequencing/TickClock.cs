using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sequencing
{
    /// <summary>固定解像度の tick とサンプル位置を同じ丸め規則で相互変換する。</summary>
    public sealed class TickClock
    {
        private const double SecondsPerMinute = 60.0;
        private readonly double _samplesPerTick;

        /// <summary>テンポとサンプルレートを指定して時間軸を作る。</summary>
        public TickClock(int tempoBpm, int sampleRate)
        {
            if (tempoBpm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tempoBpm));
            }
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            _samplesPerTick = sampleRate * SecondsPerMinute / (tempoBpm * (double)Song.FixedTicksPerBeat);
        }

        /// <summary>tick を最寄りのサンプル位置に変換する。中間値はゼロから遠ざける。</summary>
        public long TickToSamples(double tick)
        {
            if (tick < 0.0 || double.IsNaN(tick) || double.IsInfinity(tick))
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }
            double positionSamples = Math.Round(tick * _samplesPerTick, MidpointRounding.AwayFromZero);
            if (positionSamples >= long.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }
            return (long)positionSamples;
        }

        /// <summary>サンプル位置を丸めずに tick へ変換する。</summary>
        public double SamplesToTick(long positionSamples)
        {
            if (positionSamples < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionSamples));
            }
            return positionSamples / _samplesPerTick;
        }
    }
}
