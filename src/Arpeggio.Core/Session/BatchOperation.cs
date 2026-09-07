using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Session
{
    /// <summary>操作ごとの必須項目を適用時に検証する一括編集入力。</summary>
    public sealed class BatchOperation
    {
        /// <summary>操作種別。</summary>
        public BatchOperationKind Kind { get; set; }
        /// <summary>0 始まりのトラック。省略時は呼び出し側の既定値を使う。</summary>
        public int? Track { get; set; }
        /// <summary>対象または追加ノートの開始 tick。</summary>
        public int? Tick { get; set; }
        /// <summary>移動先の開始 tick。</summary>
        public int? ToTick { get; set; }
        /// <summary>ノートの長さ。</summary>
        public int? DurationTicks { get; set; }
        /// <summary>MIDI 音高。</summary>
        public int? MidiNote { get; set; }
        /// <summary>音量。</summary>
        public int? Volume { get; set; }
        /// <summary>参照または削除する音色 ID。</summary>
        public int? InstrumentId { get; set; }
        /// <summary>置換するエフェクト列。省略時は保持する。</summary>
        public NoteEffect[]? Effects { get; set; }
        /// <summary>追加・置換する音色全体。</summary>
        public Instrument? Instrument { get; set; }
        /// <summary>新しいテンポ。</summary>
        public int? TempoBpm { get; set; }
        /// <summary>新しい曲の長さ。</summary>
        public int? LengthTicks { get; set; }
        /// <summary>新しいループ開始位置。</summary>
        public int? LoopStartTick { get; set; }
        /// <summary>ミュート状態。</summary>
        public bool? Muted { get; set; }
        /// <summary>定位（-1〜1）。</summary>
        public double? Pan { get; set; }
    }
}
