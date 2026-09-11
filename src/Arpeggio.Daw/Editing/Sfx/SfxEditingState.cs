namespace Arpeggio.Daw.Editing.Sfx
{
    /// <summary>候補入力と確定の状態。同期可否の詳細は Core の同期状態で表す。</summary>
    public enum SfxEditingState
    {
        /// <summary>確定済みで入力を待っている。</summary>
        None = 0,
        /// <summary>一操作の途中値を編集中。</summary>
        Dragging = 1,
        /// <summary>入力が無効で最後の有効値を保持している。</summary>
        Invalid = 2,
        /// <summary>文書の変更により確定できない。</summary>
        Conflict = 3,
        /// <summary>保存に失敗し再試行できる。</summary>
        SaveFailed = 4,
        /// <summary>購読と入力の寿命を終了した。</summary>
        Disposed = 5
    }
}
