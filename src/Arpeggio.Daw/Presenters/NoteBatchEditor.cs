using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>複数ノートの交換を既存 Core バッチの一回の保存で公開する。</summary>
    internal static class NoteBatchEditor
    {
        internal static void Replace(EditSession session, int track, IEnumerable<Note> removed, IEnumerable<Note> added)
        {
            List<BatchOperation> operations = new List<BatchOperation>();
            // 全削除を先行させ、隣接した選択同士の移動を中間状態の重複で拒否しない。
            foreach (Note note in removed)
            {
                operations.Add(new BatchOperation { Kind = BatchOperationKind.RemoveNote, Tick = note.Tick });
            }
            foreach (Note note in added)
            {
                operations.Add(new BatchOperation
                {
                    Kind = BatchOperationKind.AddNote, Tick = note.Tick, DurationTicks = note.DurationTicks,
                    MidiNote = note.MidiNote, Volume = note.Volume, InstrumentId = note.InstrumentId,
                    Effects = (NoteEffect[])note.Effects.Clone()
                });
            }
            if (operations.Count > 0) { BatchOperationApplier.Apply(session, operations, track); }
        }
        internal static Note Copy(Note note) => new Note
        {
            Tick = note.Tick, DurationTicks = note.DurationTicks, MidiNote = note.MidiNote,
            Volume = note.Volume, InstrumentId = note.InstrumentId, Effects = (NoteEffect[])note.Effects.Clone()
        };
    }
}
