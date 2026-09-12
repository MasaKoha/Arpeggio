using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Presenters.PianoRoll.Selection
{
    /// <summary>描画座標に依存しない矩形選択範囲。</summary>
    /// <param name="StartTick">押下位置の tick。</param>
    /// <param name="StartPitch">押下位置の音高。</param>
    /// <param name="EndTick">現在位置の tick。</param>
    /// <param name="EndPitch">現在位置の音高。</param>
    public readonly record struct NoteSelectionRectangle(double StartTick, int StartPitch, double EndTick, int EndPitch)
    {
        /// <summary>左端 tick。</summary>
        public double Left => Math.Min(StartTick, EndTick);
        /// <summary>右端 tick。</summary>
        public double Right => Math.Max(StartTick, EndTick);
        /// <summary>最低音。</summary>
        public int BottomPitch => Math.Min(StartPitch, EndPitch);
        /// <summary>最高音。</summary>
        public int TopPitch => Math.Max(StartPitch, EndPitch);
        /// <summary>境界への接触と部分重なりを含めて判定する。</summary>
        public bool Intersects(Note note) => note.Tick <= Right && (long)note.Tick + note.DurationTicks >= Left &&
            note.MidiNote >= BottomPitch && note.MidiNote <= TopPitch;
    }
}
