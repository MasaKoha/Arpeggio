namespace Arpeggio.Formats.Midi
{
    /// <summary>MIDI の同時発音が出力声数を超えた場合の処理。</summary>
    public enum MidiPolyphonyMode
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>既存の最も古い発音を打ち切る。</summary>
        StealOldest = 1,
        /// <summary>既存音を保ち、新しい発音を破棄する。</summary>
        DropNew = 2
    }
}
