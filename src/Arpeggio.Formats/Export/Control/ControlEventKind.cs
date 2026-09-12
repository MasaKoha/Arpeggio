namespace Arpeggio.Formats.Export.Control
{
    /// <summary>制御時刻に確定する発音状態の遷移。</summary>
    public enum ControlEventKind
    {
        /// <summary>イベントなし。</summary>
        None = 0,
        /// <summary>ゲートを停止する。</summary>
        NoteOff = 1,
        /// <summary>先頭のマクロ値で発音する。</summary>
        NoteOn = 2,
        /// <summary>継続音のマクロ適用後の値を更新する。</summary>
        Update = 3
    }
}
