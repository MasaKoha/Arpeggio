using System;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>サンプルレートから独立して LFSR をクロック駆動する。</summary>
    internal sealed class NoiseOscillator
    {
        private int _register = 1;
        private double _phase;

        internal void Reset()
        {
            _register = 1;
            _phase = 0;
        }

        internal double Read(double increment, int width, int tap)
        {
            // perf: レジスタと位相だけを更新し、乱数器や波形配列を生成しない。
            _phase += increment;
            int steps = (int)Math.Floor(_phase);
            _phase -= steps;
            for (int index = 0; index < steps; index++)
            {
                int feedback = (_register ^ (_register >> tap)) & 1;
                _register = (_register >> 1) | (feedback << (width - 1));
            }
            return (_register & 1) == 0 ? 1 : -1;
        }
    }
}
