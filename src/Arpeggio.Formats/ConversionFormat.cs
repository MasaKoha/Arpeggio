namespace Arpeggio.Formats
{
    /// <summary>変換先または取り込み元のファイル形式。</summary>
    public enum ConversionFormat
    {
        /// <summary>形式未指定。</summary>
        None = 0,
        /// <summary>NES の NSF v1。</summary>
        Nsf = 1,
        /// <summary>NES / GB の VGM v1.71。</summary>
        Vgm = 2,
        /// <summary>SMF の MIDI 取り込み。</summary>
        Midi = 3
    }
}
