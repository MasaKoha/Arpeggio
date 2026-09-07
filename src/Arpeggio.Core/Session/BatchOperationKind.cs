namespace Arpeggio.Core.Session
{
    /// <summary>一括編集で指定できる操作。</summary>
    public enum BatchOperationKind
    {
        /// <summary>未指定。適用時は拒否する。</summary>
        None = 0,
        /// <summary>ノートを追加する。</summary>
        AddNote = 1,
        /// <summary>ノートを削除する。</summary>
        RemoveNote = 2,
        /// <summary>ノートを移動する。</summary>
        MoveNote = 3,
        /// <summary>ノートの長さを変える。</summary>
        ResizeNote = 4,
        /// <summary>指定したノート項目を更新する。</summary>
        UpdateNote = 5,
        /// <summary>音色を追加する。</summary>
        AddInstrument = 6,
        /// <summary>音色全体を置き換える。</summary>
        UpdateInstrument = 7,
        /// <summary>音色を削除する。</summary>
        RemoveInstrument = 8,
        /// <summary>テンポを設定する。</summary>
        SetTempo = 9,
        /// <summary>曲の長さを設定する。</summary>
        SetLength = 10,
        /// <summary>ループ開始位置を設定する。</summary>
        SetLoopStart = 11,
        /// <summary>トラックのミュートを設定する。</summary>
        SetTrackMuted = 12,
        /// <summary>トラックの定位を設定する。</summary>
        SetTrackPan = 13
    }
}
