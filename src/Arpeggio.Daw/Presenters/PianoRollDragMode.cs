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
        Resize = 2,
        /// <summary>追加したノートの長さを決める。</summary>
        Create = 3,
        /// <summary>矩形で選択する。</summary>
        Select = 4,
        /// <summary>軌跡上のノートを削除する。</summary>
        Erase = 5
    }
}
