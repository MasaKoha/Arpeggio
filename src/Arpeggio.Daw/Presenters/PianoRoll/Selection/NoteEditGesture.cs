using System;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters.PianoRoll.Selection
{
    /// <summary>途中編集を公開しつつ、押下から解放までを一履歴にまとめる。</summary>
    internal sealed class NoteEditGesture
    {
        private readonly DawDocument document;
        private Song? snapshot;
        private Song[] undo = Array.Empty<Song>();
        private Song[] redo = Array.Empty<Song>();
        internal NoteEditGesture(DawDocument document) { this.document = document; }
        internal void Begin()
        {
            End();
            snapshot = SongSerializer.Deserialize(SongSerializer.Serialize(document.Song));
            undo = document.Session.History.GetUndoSnapshots();
            redo = document.Session.History.GetRedoSnapshots();
        }
        internal void End()
        {
            if (snapshot == null) { return; }
            bool hasChanged = SongSerializer.Serialize(snapshot) != SongSerializer.Serialize(document.Song);
            document.Session.History.Restore(undo, redo);
            if (hasChanged) { document.Session.History.Record(snapshot); }
            snapshot = null;
            undo = Array.Empty<Song>();
            redo = Array.Empty<Song>();
        }
    }
}
