namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>共通レート表で駆動する決定的な 15 bit ノイズ。</summary>
    public sealed class SnesNoiseGenerator
    {
        private const int InitialRegister = 0x4000;
        private const int FeedbackBit = 14;
        private const int SignBit = 0x4000;
        private const int RegisterRange = 0x8000;
        private const double OutputScale = 16384.0;
        private int _register = InitialRegister;
        private int _remainingSamples;

        /// <summary>発音開始時の状態へ戻す。</summary>
        public void Reset()
        {
            _register = InitialRegister;
            _remainingSamples = 0;
        }

        /// <summary>一 DSP サンプル進め、signed 15 bit 相当の正規化出力を返す。</summary>
        public double ReadSample(int rate)
        {
            // perf: LFSR と周期カウンターだけを更新し、乱数オブジェクトを作らない。
            int period = SnesRateTable.GetPeriod(rate);
            if (period > 0 && --_remainingSamples <= 0)
            {
                int feedback = (_register ^ (_register >> 1)) & 1;
                _register = (_register >> 1) | (feedback << FeedbackBit);
                _remainingSamples = period;
            }
            int signedValue = (_register & SignBit) == 0 ? _register : _register - RegisterRange;
            return signedValue / OutputScale;
        }
    }
}
