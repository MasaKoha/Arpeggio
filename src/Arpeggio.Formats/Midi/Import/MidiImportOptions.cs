using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>SMF を新規 Song へ変換する設定。入力名・時間量子化・声割り当て・保存可否を指定する。</summary>
    public sealed class MidiImportOptions
    {
        /// <summary>出力チップ。明示指定を必須とする。</summary>
        public ChipKind Chip { get; init; }
        /// <summary>明示する基準 BPM。null は入力の先頭有効テンポを使う。</summary>
        public int? Tempo { get; init; }
        /// <summary>量子化グリッド。48 の正の約数を指定する。</summary>
        public int QuantizeTicks { get; init; } = 1;
        /// <summary>声数不足時の処理。</summary>
        public MidiPolyphonyMode Polyphony { get; init; } = MidiPolyphonyMode.StealOldest;
        /// <summary>1〜16 の MIDI チャンネルから出力トラック候補への上書き。空候補は明示除外。</summary>
        public IReadOnlyDictionary<int, IReadOnlyList<int>>? ChannelMap { get; init; }
        /// <summary>出力曲名の明示指定。null は入力メタデータと SourceName で決める。</summary>
        public string? Title { get; init; }
        /// <summary>Stream 入力の曲名フォールバックに使う入力名。</summary>
        public string SourceName { get; init; } = "midi";
        /// <summary>変換警告が一件でもあれば保存を拒否する。</summary>
        public bool Strict { get; init; }
    }
}
