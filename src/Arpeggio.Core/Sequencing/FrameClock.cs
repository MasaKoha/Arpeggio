using System;

namespace Arpeggio.Core.Sequencing
{
    /// <summary>整数のサンプル位置から 60 Hz のフレーム境界を求める。</summary>
    public sealed class FrameClock
    {
        /// <summary>マクロとエンベロープが進む固定フレームレート。</summary>
        public const int FramesPerSecond = 60;
        private readonly int _sampleRate;

        /// <summary>サンプルレートを指定してフレーム時間軸を作る。</summary>
        public FrameClock(int sampleRate)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            _sampleRate = sampleRate;
        }

        /// <summary>指定位置が属するゼロ始まりのフレーム番号を返す。</summary>
        public long GetFrame(long positionSamples)
        {
            if (positionSamples < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionSamples));
            }
            // perf: 分数周期を毎回整数から求め、端数の累積誤差と一時オブジェクトを避ける。
            return checked(positionSamples / _sampleRate * FramesPerSecond
                + positionSamples % _sampleRate * FramesPerSecond / _sampleRate);
        }

        /// <summary>指定位置の次のフレームが始まる最初のサンプル位置を返す。</summary>
        public long GetNextBoundary(long positionSamples)
        {
            long nextFrame = checked(GetFrame(positionSamples) + 1);
            return checked(nextFrame / FramesPerSecond * _sampleRate
                + (nextFrame % FramesPerSecond * _sampleRate + FramesPerSecond - 1) / FramesPerSecond);
        }
    }
}
