namespace Arpeggio.Core.Sfx
{
    /// <summary>Game Boy のパルスとノイズの設定。</summary>
    public sealed record SfxGameBoyParameters
    {
        /// <summary>デューティ比（12.5 / 25 / 50 / 75%）。</summary>
        public double DutyPercent { get; init; } = 25;

        /// <summary>デューティ変化（-100〜100 %ポイント/秒）。</summary>
        public double DutySweepPercentPerSecond { get; init; } = 0;

        /// <summary>LFSR のビット幅（7 / 15）。</summary>
        public int NoiseWidth { get; init; } = 15;

        /// <summary>合成器の周期選択値（0〜127）。NR43 の raw byte ではない。</summary>
        public int NoiseSelection { get; init; } = 96;

        /// <summary>周期選択の速度（-240〜240 選択値/秒）。</summary>
        public double NoiseSlideSelectionsPerSecond { get; init; } = 0;
    }
}
