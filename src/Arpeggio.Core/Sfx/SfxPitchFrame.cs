namespace Arpeggio.Core.Sfx
{
    /// <summary>終端ゼロを含む一制御フレームの要求音程とチップ量子化結果。</summary>
    public sealed record SfxPitchFrame
    {
        /// <summary>発音先頭を0とする制御フレーム。</summary>
        public int Frame { get; init; }

        /// <summary>anchor と二つのマクロを加算した有限の要求 MIDI 音程。</summary>
        public double RequestedMidiNote { get; init; }

        /// <summary>要求周波数。正の double として表現できない場合だけ null。</summary>
        public double? RequestedFrequencyHz { get; init; }

        /// <summary>音域制限と周期レジスタへの量子化後の周波数。</summary>
        public double ActualFrequencyHz { get; init; }

        /// <summary>通常の周期量子化とは別に、発音可能音域で制限されたか。</summary>
        public bool IsClamped { get; init; }
    }
}
