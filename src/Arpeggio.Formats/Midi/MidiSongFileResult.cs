namespace Arpeggio.Formats.Midi
{
    /// <summary>新規保存または事前診断の結果。保存先の存在は変換診断と区別する。</summary>
    public sealed class MidiSongFileResult
    {
        internal MidiSongFileResult(bool written, bool destinationExists, ConversionReport report)
        {
            Written = written;
            DestinationExists = destinationExists;
            Report = report;
        }

        /// <summary>今回の呼び出しで新規ファイルを確定したか。dry-run と変換拒否では false。</summary>
        public bool Written { get; }
        /// <summary>処理開始時点で保存先が存在したか。dry-run でも確認できる。</summary>
        public bool DestinationExists { get; }
        /// <summary>元の全変換診断。保存サイズは実保存量ではなく確定した予定値。</summary>
        public ConversionReport Report { get; }
    }
}
