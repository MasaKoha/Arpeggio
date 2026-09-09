using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Sfx
{
    /// <summary>NES のパルスとノイズの設定。</summary>
    public sealed record SfxNesParameters
    {
        /// <summary>デューティ比（12.5 / 25 / 50 / 75%）。</summary>
        public double DutyPercent { get; init; } = 25;

        /// <summary>デューティ変化（-100〜100 %ポイント/秒）。</summary>
        public double DutySweepPercentPerSecond { get; init; } = 0;

        /// <summary>固定の長周期・短周期モード。</summary>
        public NoiseMode NoiseMode { get; init; } = NoiseMode.Long;

        /// <summary>ノイズ周期表の添字（0〜15）。</summary>
        public int NoisePeriodIndex { get; init; } = 12;

        /// <summary>周期添字の速度（-60〜60 添字/秒）。</summary>
        public double NoiseSlideIndicesPerSecond { get; init; } = 0;
    }
}
