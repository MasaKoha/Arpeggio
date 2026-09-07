using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>32 kHz の左右独立ディレイと 8 タップ FIR フィードバック。</summary>
    public sealed class SnesEcho
    {
        private const int MillisecondsPerSecond = 1000;
        private const int DelayStepMilliseconds = 16;
        private const int MaximumDelayMilliseconds = 240;
        private const int TapCount = 8;
        private const double CoefficientScale = 128.0;
        private readonly float[] _left;
        private readonly float[] _right;
        private readonly float[] _historyLeft = new float[TapCount];
        private readonly float[] _historyRight = new float[TapCount];
        private readonly int[] _coefficients;
        private readonly double _feedback;
        private readonly double _volume;
        private readonly bool _enabled;
        private readonly int _sampleRate;
        private int _position;
        private int _historyPosition;
        private long _clock;
        private float _previousLeft;
        private float _previousRight;
        private float _currentLeft;
        private float _currentRight;

        /// <summary>遅延を 16 ms 単位に丸め、32 kHz の履歴を事前確保する。</summary>
        public SnesEcho(int sampleRate, SnesEchoSettings settings)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            if (settings.FirCoefficients is null || settings.FirCoefficients.Length != TapCount)
            {
                throw new ArgumentException("FIR 係数は 8 要素です。", nameof(settings));
            }
            _coefficients = (int[])settings.FirCoefficients.Clone();
            foreach (int coefficient in _coefficients)
            {
                if (coefficient < sbyte.MinValue || coefficient > sbyte.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(settings), "FIR 係数は -128〜127 です。");
                }
            }
            int delay = Math.Clamp((int)Math.Round(settings.DelayMilliseconds / (double)DelayStepMilliseconds), 0,
                MaximumDelayMilliseconds / DelayStepMilliseconds) * DelayStepMilliseconds;
            int length = Math.Max(1, delay * SnesRateTable.SampleRate / MillisecondsPerSecond);
            _left = new float[length];
            _right = new float[length];
            _feedback = settings.Feedback;
            _volume = settings.Volume;
            _enabled = delay > 0;
            _sampleRate = sampleRate;
            _clock = sampleRate;
        }

        /// <summary>一出力フレームの送り音を受け、FIR 処理した遅延音を返す。</summary>
        public void Process(float sendLeft, float sendRight, out float wetLeft, out float wetRight)
        {
            // perf: 32 kHz のミキサーからは変換を挟まず、固定長の履歴を直接進める。
            if (_sampleRate == SnesRateTable.SampleRate)
            {
                ProcessDsp(sendLeft, sendRight, out wetLeft, out wetRight);
                return;
            }
            while (_clock >= _sampleRate)
            {
                _clock -= _sampleRate;
                _previousLeft = _currentLeft;
                _previousRight = _currentRight;
                ProcessDsp(sendLeft, sendRight, out _currentLeft, out _currentRight);
            }
            double fraction = (double)_clock / _sampleRate;
            wetLeft = (float)(_previousLeft + (_currentLeft - _previousLeft) * fraction);
            wetRight = (float)(_previousRight + (_currentRight - _previousRight) * fraction);
            _clock += SnesRateTable.SampleRate;
        }

        /// <summary>停止・シーク時に FIR と遅延・補間の履歴をすべて消す。</summary>
        public void Reset()
        {
            Array.Clear(_left, 0, _left.Length);
            Array.Clear(_right, 0, _right.Length);
            Array.Clear(_historyLeft, 0, TapCount);
            Array.Clear(_historyRight, 0, TapCount);
            _position = 0;
            _historyPosition = 0;
            _clock = _sampleRate;
            _previousLeft = 0;
            _previousRight = 0;
            _currentLeft = 0;
            _currentRight = 0;
        }

        private void ProcessDsp(float sendLeft, float sendRight, out float wetLeft, out float wetRight)
        {
            // perf: FIR は八回の積和で完結し、送りと左右履歴を再利用する。
            wetLeft = 0;
            wetRight = 0;
            if (!_enabled)
            {
                return;
            }
            _historyLeft[_historyPosition] = _left[_position];
            _historyRight[_historyPosition] = _right[_position];
            double filteredLeft = 0;
            double filteredRight = 0;
            for (int tap = 0; tap < TapCount; tap++)
            {
                int history = (_historyPosition - tap + TapCount) % TapCount;
                filteredLeft += _historyLeft[history] * _coefficients[tap] / CoefficientScale;
                filteredRight += _historyRight[history] * _coefficients[tap] / CoefficientScale;
            }
            float delayedLeft = SnesMixer.Clamp16(filteredLeft);
            float delayedRight = SnesMixer.Clamp16(filteredRight);
            _left[_position] = SnesMixer.Clamp16(sendLeft + delayedLeft * _feedback);
            _right[_position] = SnesMixer.Clamp16(sendRight + delayedRight * _feedback);
            wetLeft = (float)(delayedLeft * _volume);
            wetRight = (float)(delayedRight * _volume);
            _position = (_position + 1) % _left.Length;
            _historyPosition = (_historyPosition + 1) % TapCount;
        }
    }
}
