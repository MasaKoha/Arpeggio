using Arpeggio.Core.Document;
using Arpeggio.Formats.Midi;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Daw.Presenters.Midi
{
    /// <summary>MIDI 取り込み画面の入力を開始時に固定する。</summary>
    public sealed class MidiImportInput
    {
        /// <summary>読み込む SMF のローカルパス。</summary>
        public string SourcePath { get; init; } = string.Empty;
        /// <summary>新規 JSON の保存先。</summary>
        public string DestinationPath { get; init; } = string.Empty;
        /// <summary>出力チップ。</summary>
        public ChipKind Chip { get; init; } = ChipKind.Nes;
        /// <summary>基準 BPM。空欄は MIDI の先頭有効テンポ。</summary>
        public string Tempo { get; init; } = string.Empty;
        /// <summary>48 の正の約数で指定する量子化幅。</summary>
        public string QuantizeTicks { get; init; } = "1";
        /// <summary>声数不足時の処理。</summary>
        public MidiPolyphonyMode Polyphony { get; init; } = MidiPolyphonyMode.StealOldest;
        /// <summary>任意の UTF-8 JSON map ファイル。空欄は自動割り当て。</summary>
        public string ChannelMapPath { get; init; } = string.Empty;
        /// <summary>任意の曲名。空欄は MIDI と入力名から決定する。</summary>
        public string Title { get; init; } = string.Empty;
        /// <summary>変換警告があれば保存を拒否する。</summary>
        public bool Strict { get; init; }
    }
}
