using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>押下時点を基準にした複数ドラッグと選択・削除の軌跡を保持する。</summary>
    internal sealed class PianoRollGesture
    {
        private const double PitchHalfHeight = 0.5;
        private readonly DawDocument document;
        private readonly NoteSelection selection;
        private readonly NoteEditGesture history;
        private Note[] originalNotes = Array.Empty<Note>();
        private Note? anchor;
        private int anchorIndex;
        private double startTick;
        private int startPitch;
        private double previousTick;
        private int previousPitch;
        internal PianoRollGesture(DawDocument document, NoteSelection selection)
        {
            this.document = document;
            this.selection = selection;
            history = new NoteEditGesture(document);
        }
        internal PianoRollDragMode Mode { get; private set; }
        internal NoteSelectionRectangle? Rectangle { get; private set; }
        internal void BeginHistory() => history.Begin();
        internal void Begin(Note hit, Note[] selected, double tick, int pitch, PianoRollDragMode mode)
        {
            originalNotes = selected.Select(NoteBatchEditor.Copy).ToArray();
            anchor = NoteBatchEditor.Copy(hit);
            anchorIndex = Array.FindIndex(originalNotes, note => note.Tick == hit.Tick);
            startTick = tick;
            startPitch = pitch;
            Mode = mode;
        }
        internal void BeginSelection(double tick, int pitch)
        {
            startTick = tick;
            startPitch = pitch;
            Mode = PianoRollDragMode.Select;
            Rectangle = new NoteSelectionRectangle(tick, pitch, tick, pitch);
            selection.Clear();
        }
        internal void BeginErase(double tick, int pitch, int track)
        {
            history.Begin();
            Mode = PianoRollDragMode.Erase;
            previousTick = tick;
            previousPitch = pitch;
            Erase(tick, pitch, track);
        }
        internal int? Drag(double tick, int pitch, int track, SnapResolution resolution, bool bypassSnap)
        {
            if (Mode == PianoRollDragMode.Select)
            {
                NoteSelectionRectangle rectangle = new NoteSelectionRectangle(startTick, startPitch, tick, pitch);
                Rectangle = rectangle;
                selection.Replace(document.Song.Tracks[track].Notes.Where(rectangle.Intersects));
                return null;
            }
            if (Mode == PianoRollDragMode.Erase) { Erase(tick, pitch, track); return null; }
            if (anchor == null) { return null; }
            Note[] replacements = CreateReplacements(tick, pitch, resolution, bypassSnap);
            Note[] current = selection.Resolve(document.Song.Tracks[track].Notes);
            if (AreEqual(current, replacements)) { return replacements[anchorIndex].DurationTicks; }
            NoteBatchEditor.Replace(document.Session, track, current, replacements);
            selection.Replace(replacements);
            return replacements[anchorIndex].DurationTicks;
        }
        internal void End()
        {
            history.End();
            Mode = PianoRollDragMode.None;
            Rectangle = null;
            anchor = null;
            originalNotes = Array.Empty<Note>();
        }
        private Note[] CreateReplacements(double tick, int pitch, SnapResolution resolution, bool bypassSnap)
        {
            Note originalAnchor = anchor!;
            int tickDelta = 0;
            int pitchDelta = 0;
            int durationDelta = 0;
            if (Mode == PianoRollDragMode.Move)
            {
                tickDelta = SnapGrid.Snap(originalAnchor.Tick + tick - startTick, resolution, bypassSnap) - originalAnchor.Tick;
                pitchDelta = pitch - startPitch;
            }
            else if (Mode == PianoRollDragMode.Create)
            {
                // 押下直後は記憶した長さを保ち、横方向へ動き始めたらポインターを終端にする。
                if (tick != startTick)
                {
                    durationDelta = SnapGrid.Snap(tick, resolution, bypassSnap) - originalAnchor.Tick - originalAnchor.DurationTicks;
                }
            }
            else
            {
                durationDelta = SnapGrid.Snap(originalAnchor.Tick + originalAnchor.DurationTicks + tick - startTick,
                    resolution, bypassSnap) - originalAnchor.Tick - originalAnchor.DurationTicks;
            }
            Note[] replacements = originalNotes.Select(NoteBatchEditor.Copy).ToArray();
            foreach (Note note in replacements)
            {
                note.Tick = checked(note.Tick + tickDelta);
                note.MidiNote += pitchDelta;
                note.DurationTicks = Math.Max(1, checked(note.DurationTicks + durationDelta));
            }
            return replacements;
        }
        private static bool AreEqual(Note[] current, Note[] replacements) => current.Length == replacements.Length &&
            current.Zip(replacements).All(pair => pair.First.Tick == pair.Second.Tick &&
                pair.First.MidiNote == pair.Second.MidiNote && pair.First.DurationTicks == pair.Second.DurationTicks);
        private void Erase(double tick, int pitch, int track)
        {
            Note[] removed = document.Song.Tracks[track].Notes.Where(note => Crosses(note, tick, pitch)).ToArray();
            previousTick = tick;
            previousPitch = pitch;
            if (removed.Length == 0) { return; }
            NoteBatchEditor.Replace(document.Session, track, removed, Array.Empty<Note>());
            selection.Replace(selection.Resolve(document.Song.Tracks[track].Notes));
        }
        private bool Crosses(Note note, double tick, int pitch)
        {
            double entry = 0;
            double exit = 1;
            return ClipAxis(previousTick, tick - previousTick, note.Tick, (double)note.Tick + note.DurationTicks, ref entry, ref exit) &&
                ClipAxis(previousPitch, pitch - previousPitch, note.MidiNote - PitchHalfHeight,
                    note.MidiNote + PitchHalfHeight, ref entry, ref exit);
        }
        private static bool ClipAxis(double origin, double delta, double minimum, double maximum, ref double entry, ref double exit)
        {
            if (delta == 0) { return origin >= minimum && origin < maximum; }
            double first = (minimum - origin) / delta;
            double last = (maximum - origin) / delta;
            entry = Math.Max(entry, Math.Min(first, last));
            exit = Math.Min(exit, Math.Max(first, last));
            return entry <= exit;
        }
    }
}
