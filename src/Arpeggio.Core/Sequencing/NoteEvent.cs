using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sequencing
{
    /// <summary>サンプル位置で発生する発音状態の遷移。発音前には前のノートを停止する。</summary>
    public readonly record struct NoteEvent
    {
        /// <summary>遷移位置と発音の進行状態を指定する。</summary>
        public NoteEvent(long positionSamples, Note? note, double elapsedTicks, double durationTicks, bool isNoteOn)
        {
            PositionSamples = positionSamples;
            Note = note;
            ElapsedTicks = elapsedTicks;
            DurationTicks = durationTicks;
            IsNoteOn = isNoteOn;
        }

        /// <summary>ソング先頭からの絶対サンプル位置。</summary>
        public long PositionSamples { get; init; }

        /// <summary>発音するノート。停止だけの場合は null。</summary>
        public Note? Note { get; init; }

        /// <summary>Delay 後の発音開始から経過した tick。</summary>
        public double ElapsedTicks { get; init; }

        /// <summary>Delay を除いた発音全体の長さ。</summary>
        public double DurationTicks { get; init; }

        /// <summary>停止に続いてノートを発音する場合は true。</summary>
        public bool IsNoteOn { get; init; }
    }
}
