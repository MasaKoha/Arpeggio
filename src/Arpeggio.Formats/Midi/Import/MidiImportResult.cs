using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>変換候補・確定済み JSON・全体診断。変換エラー時は候補を保持しない。</summary>
    public sealed class MidiImportResult
    {
        internal MidiImportResult(Song? song, string? json, ConversionReport report)
        {
            Song = song;
            Json = json;
            Report = report;
        }

        /// <summary>確認・編集用の独立した候補。後編集は確定済み Json や保存内容へ反映しない。</summary>
        public Song? Song { get; }
        /// <summary>検証時に確定した version 1 の正規 JSON。strict 警告時も内容を確認できる。</summary>
        public string? Json { get; }
        /// <summary>入力解析から Song 検証までの全診断と予定サイズ。</summary>
        public ConversionReport Report { get; }
        /// <summary>検証済み JSON があり、strict を含む全診断が保存を許可するか。</summary>
        public bool CanWrite => Json != null && Report.CanWrite;
    }
}
