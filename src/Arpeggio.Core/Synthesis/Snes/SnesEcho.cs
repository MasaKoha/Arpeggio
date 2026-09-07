using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>左右別の循環ディレイラインとフィードバック。</summary>
    public sealed class SnesEcho
    {
        private const int MillisecondsPerSecond = 1000;
        private readonly float[] _left;
        private readonly float[] _right;
        private readonly double _feedback;
        private readonly double _volume;
        private readonly bool _enabled;
        private int _position;

        /// <summary>ソング設定を固定してディレイラインを事前確保する。</summary>
        public SnesEcho(int sampleRate, SnesEchoSettings settings)
        {
            int length = Math.Max(1, (int)Math.Round((double)settings.DelayMilliseconds * sampleRate / MillisecondsPerSecond));
            _left = new float[length];
            _right = new float[length];
            _feedback = settings.Feedback;
            _volume = settings.Volume;
            _enabled = settings.DelayMilliseconds > 0;
        }

        /// <summary>一フレームの送り音を受け取り、遅延音を返す。</summary>
        public void Process(float sendLeft, float sendRight, out float wetLeft, out float wetRight)
        {
            wetLeft = 0;
            wetRight = 0;
            if (!_enabled)
            {
                return;
            }
            // perf: 固定長の循環バッファでコールバック中の再確保を避ける。
            float delayedLeft = _left[_position];
            float delayedRight = _right[_position];
            _left[_position] = (float)(sendLeft + delayedLeft * _feedback);
            _right[_position] = (float)(sendRight + delayedRight * _feedback);
            wetLeft = (float)(delayedLeft * _volume);
            wetRight = (float)(delayedRight * _volume);
            _position = (_position + 1) % _left.Length;
        }

        /// <summary>停止・シーク時に残響を消す。</summary>
        public void Reset()
        {
            Array.Clear(_left, 0, _left.Length);
            Array.Clear(_right, 0, _right.Length);
            _position = 0;
        }
    }
}
