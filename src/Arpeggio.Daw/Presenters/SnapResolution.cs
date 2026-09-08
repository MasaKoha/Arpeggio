namespace Arpeggio.Daw.Presenters
{
    /// <summary>ピアノロールのスナップ単位。</summary>
    public enum SnapResolution
    {
        /// <summary>整数 tick 単位。</summary>
        None = 0,
        /// <summary>全音符。</summary>
        Bar,
        /// <summary>二分音符。</summary>
        Half,
        /// <summary>四分音符。</summary>
        Quarter,
        /// <summary>八分音符。</summary>
        Eighth,
        /// <summary>十六分音符。</summary>
        Sixteenth,
        /// <summary>一拍を三分割する三連符。</summary>
        Triplet
    }
}
