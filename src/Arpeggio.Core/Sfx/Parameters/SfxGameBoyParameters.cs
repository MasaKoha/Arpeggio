using System.Text.Json.Serialization;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>Game Boy のパルスとノイズの設定。</summary>
    public sealed record SfxGameBoyParameters
    {
        /// <summary>デューティ比（12.5 / 25 / 50 / 75%）。</summary>
        [JsonPropertyOrder(0)]
        public double DutyPercent { get; init; } = 25;

        /// <summary>デューティ変化（-100〜100 %ポイント/秒）。</summary>
        [JsonPropertyOrder(1)]
        public double DutySweepPercentPerSecond { get; init; } = 0;

        /// <summary>LFSR のビット幅（7 / 15）。</summary>
        [JsonPropertyOrder(2)]
        public int NoiseWidth { get; init; } = 15;

        /// <summary>合成器の周期選択値（0〜127）。NR43 の raw byte ではない。</summary>
        [JsonPropertyOrder(3)]
        public int NoiseSelection { get; init; } = 96;

        /// <summary>周期選択の速度（-240〜240 選択値/秒）。</summary>
        [JsonPropertyOrder(4)]
        public double NoiseSlideSelectionsPerSecond { get; init; } = 0;
    }
}
