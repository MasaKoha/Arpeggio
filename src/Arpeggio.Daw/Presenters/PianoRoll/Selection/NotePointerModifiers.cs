using System;

namespace Arpeggio.Daw.Presenters.PianoRoll.Selection
{
    /// <summary>ピアノロールの選択修飾入力。</summary>
    [Flags]
    public enum NotePointerModifiers
    {
        /// <summary>通常入力。</summary>
        None = 0,
        /// <summary>空白で矩形選択する。</summary>
        Control = 1,
        /// <summary>ノートの選択を反転する。</summary>
        Shift = 2
    }
}
