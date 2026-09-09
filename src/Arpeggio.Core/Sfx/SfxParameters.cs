using System.Text.Json.Serialization;

namespace Arpeggio.Core.Sfx
{
    /// <summary>共通二声と現在チップ一つの設定。チップの正本は呼び出し側の Song。</summary>
    public sealed record SfxParameters
    {
        /// <summary>トーン一声の設定。</summary>
        [JsonPropertyOrder(0)]
        public SfxToneParameters Tone { get; init; } = new SfxToneParameters();

        /// <summary>ノイズ一声の設定。</summary>
        [JsonPropertyOrder(1)]
        public SfxNoiseParameters Noise { get; init; } = new SfxNoiseParameters();

        /// <summary>NES の場合だけ指定する設定。</summary>
        [JsonPropertyOrder(2)]
        public SfxNesParameters? Nes { get; init; }

        /// <summary>Game Boy の場合だけ指定する設定。</summary>
        [JsonPropertyOrder(3)]
        public SfxGameBoyParameters? GameBoy { get; init; }

        /// <summary>SNES の場合だけ指定する設定。</summary>
        [JsonPropertyOrder(4)]
        public SfxSnesParameters? Snes { get; init; }
    }
}
