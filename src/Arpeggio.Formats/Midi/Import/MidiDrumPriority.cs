namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>音色変換側で分類済みの同時打撃優先順位。小さい値から採用する。</summary>
    public enum MidiDrumPriority
    {
        /// <summary>旋律。打楽器分類なし。</summary>
        None = 0,
        /// <summary>キック。</summary>
        Kick = 1,
        /// <summary>スネア。未知ドラムのフォールバックも含む。</summary>
        Snare = 2,
        /// <summary>タム。</summary>
        Tom = 3,
        /// <summary>クラッシュ。</summary>
        Crash = 4,
        /// <summary>オープンハイハット。</summary>
        OpenHat = 5,
        /// <summary>クローズドハイハット。</summary>
        Hat = 6
    }
}
