using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>選択ノートの音量を押下時からの同じ差分で変更する。</summary>
    public sealed class VelocityLanePresenter
    {
        /// <summary>チップ共通の最大音量。</summary>
        public const int MaximumVolume = 15;
        private readonly DawDocument document;
        private readonly NoteSelection selection;
        private readonly Action changed;
        private readonly NoteEditGesture history;
        private Note[] originalNotes = Array.Empty<Note>();
        private int startVolume;
        private int track;
        /// <summary>ピアノロールと選択・履歴の境界を共有する。</summary>
        public VelocityLanePresenter(DawDocument document, NoteSelection selection, Action changed)
        {
            this.document = document;
            this.selection = selection;
            this.changed = changed;
            history = new NoteEditGesture(document);
        }
        /// <summary>有効な棒からドラッグしているか。</summary>
        public bool IsDragging => originalNotes.Length > 0;
        /// <summary>tick が最寄りの棒を掴み、選択全体の音量変更を始める。</summary>
        public void Press(int trackIndex, double tick, double volume, double toleranceTicks)
        {
            EndDrag();
            track = trackIndex;
            Note? hit = document.Song.Tracks[track].Notes
                .Where(note => Math.Abs(note.Tick - tick) <= toleranceTicks)
                .OrderBy(note => Math.Abs(note.Tick - tick)).FirstOrDefault();
            if (hit == null) { return; }
            if (!selection.Contains(hit.Tick)) { selection.Select(hit.Tick); }
            originalNotes = selection.Resolve(document.Song.Tracks[track].Notes).Select(NoteBatchEditor.Copy).ToArray();
            startVolume = hit.Volume;
            history.Begin();
            Drag(volume);
            changed();
        }
        /// <summary>音量 0〜15 の差分を全選択へ適用する。</summary>
        public void Drag(double volume)
        {
            if (!IsDragging) { return; }
            int delta = RoundVolume(volume) - startVolume;
            Note[] replacements = originalNotes.Select(NoteBatchEditor.Copy).ToArray();
            foreach (Note note in replacements) { note.Volume = Math.Clamp(note.Volume + delta, 0, MaximumVolume); }
            Note[] current = selection.Resolve(document.Song.Tracks[track].Notes);
            if (current.Zip(replacements).All(pair => pair.First.Volume == pair.Second.Volume)) { return; }
            NoteBatchEditor.Replace(document.Session, track, current, replacements);
            changed();
        }
        /// <summary>音量ドラッグ全体を一履歴へ確定する。</summary>
        public void EndDrag()
        {
            history.End();
            originalNotes = Array.Empty<Note>();
        }
        private static int RoundVolume(double volume) => (int)Math.Round(Math.Clamp(volume, 0, MaximumVolume), MidpointRounding.AwayFromZero);
    }
}
