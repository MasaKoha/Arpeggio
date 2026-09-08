using System;
using System.Linq;
using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>相対 tick と全ノート属性を保持する同一文書・トラック専用クリップボード。</summary>
    public sealed class NoteClipboard
    {
        private Song? sourceSong;
        private int sourceTrack;
        private Note[] notes = Array.Empty<Note>();
        /// <summary>選択範囲の開始から終端までの長さ。</summary>
        public int LengthTicks { get; private set; }
        /// <summary>元の編集や効果配列から独立したコピーを保存する。</summary>
        public void Copy(Song song, int track, Note[] selected)
        {
            if (selected.Length == 0) { return; }
            sourceSong = song;
            sourceTrack = track;
            int origin = selected.Min(note => note.Tick);
            LengthTicks = selected.Max(note => note.Tick + note.DurationTicks) - origin;
            notes = selected.Select(NoteBatchEditor.Copy).ToArray();
            foreach (Note note in notes) { note.Tick -= origin; }
        }
        /// <summary>元と同じトラックでのみ指定位置の独立したノート列を作る。</summary>
        public Note[] CreatePaste(Song song, int track, int tick)
        {
            if (notes.Length == 0) { return Array.Empty<Note>(); }
            if (!ReferenceEquals(song, sourceSong) || track != sourceTrack)
            {
                throw new InvalidOperationException("貼り付けはコピー元と同じトラックで行ってください。");
            }
            Note[] pasted = notes.Select(NoteBatchEditor.Copy).ToArray();
            foreach (Note note in pasted) { note.Tick = checked(note.Tick + tick); }
            return pasted;
        }
    }
}
