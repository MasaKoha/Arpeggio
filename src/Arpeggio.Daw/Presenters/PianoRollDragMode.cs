namespace Arpeggio.Daw.Presenters
{
    /// <summary>ピアノロールの排他的な操作状態。</summary>
    public enum PianoRollDragMode
    {
        /// <summary>操作していない。</summary>
        None = 0,
        /// <summary>開始位置と音高を変更する。</summary>
        Move = 1,
        /// <summary>右端を変更する。</summary>
        Resize = 2
    }
}
