namespace Arpeggio.Core.Sfx
{
    /// <summary>共通二声と現在チップ一つの設定。チップの正本は呼び出し側の Song。</summary>
    public sealed record SfxParameters
    {
        /// <summary>トーン一声の設定。</summary>
        public SfxToneParameters Tone { get; init; } = new SfxToneParameters();

        /// <summary>ノイズ一声の設定。</summary>
        public SfxNoiseParameters Noise { get; init; } = new SfxNoiseParameters();

        /// <summary>NES の場合だけ指定する設定。</summary>
        public SfxNesParameters? Nes { get; init; }

        /// <summary>Game Boy の場合だけ指定する設定。</summary>
        public SfxGameBoyParameters? GameBoy { get; init; }

        /// <summary>SNES の場合だけ指定する設定。</summary>
        public SfxSnesParameters? Snes { get; init; }
    }
}
