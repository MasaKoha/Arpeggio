namespace Arpeggio.Daw.Audio.Sfx
{
    /// <summary>試聴の要求から停止・破棄までの状態。</summary>
    public enum SfxPreviewState
    {
        /// <summary>停止中。</summary>
        None = 0,
        /// <summary>最新世代のレンダラーを準備中。</summary>
        Generating = 1,
        /// <summary>試聴出力を所有して再生中。</summary>
        Playing = 2,
        /// <summary>準備または出力に失敗した。</summary>
        Failed = 3,
        /// <summary>全購読と試聴参照を解放した。</summary>
        Disposed = 4
    }
}
