using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Session
{
    /// <summary>ノート列の順序を保つセッション編集操作。</summary>
    public sealed class NoteEditor
    {
        private readonly EditSession _session;

        /// <summary>編集対象のセッションを指定する。</summary>
        public NoteEditor(EditSession session)
        {
            _session = session;
        }

        /// <summary>ノートの独立したコピーを追加し、開始 tick 順を保つ。</summary>
        public void Add(int trackIndex, Note note)
        {
            _session.Change(song =>
            {
                Track track = EditSession.GetTrack(song, trackIndex);
                track.Notes.Add(Copy(note));
                track.Notes.Sort(CompareTicks);
            });
        }

        /// <summary>指定開始 tick のノートを削除する。</summary>
        public void Remove(int trackIndex, int tick)
        {
            _session.Change(song =>
            {
                Track track = EditSession.GetTrack(song, trackIndex);
                track.Notes.Remove(GetNote(track, tick));
            });
        }

        /// <summary>ノートの開始位置と必要なら音高を変更する。</summary>
        public void Move(int trackIndex, int tick, int newTick, int? midiNote = null)
        {
            _session.Change(song =>
            {
                Track track = EditSession.GetTrack(song, trackIndex);
                Note note = GetNote(track, tick);
                note.Tick = newTick;
                if (midiNote.HasValue)
                {
                    note.MidiNote = midiNote.Value;
                }
                track.Notes.Sort(CompareTicks);
            });
        }

        /// <summary>ノートの長さを変更する。</summary>
        public void Resize(int trackIndex, int tick, int durationTicks)
        {
            _session.Change(song => GetNote(EditSession.GetTrack(song, trackIndex), tick).DurationTicks = durationTicks);
        }

        /// <summary>ノートの音量を変更する。</summary>
        public void SetVolume(int trackIndex, int tick, int volume)
        {
            _session.Change(song => GetNote(EditSession.GetTrack(song, trackIndex), tick).Volume = volume);
        }

        /// <summary>指定開始 tick のノートを新しい内容に置き換える。</summary>
        public void Update(int trackIndex, int tick, Note replacement)
        {
            _session.Change(song =>
            {
                Track track = EditSession.GetTrack(song, trackIndex);
                int noteIndex = track.Notes.IndexOf(GetNote(track, tick));
                track.Notes[noteIndex] = Copy(replacement);
                track.Notes.Sort(CompareTicks);
            });
        }

        private static Note GetNote(Track track, int tick)
        {
            foreach (Note note in track.Notes)
            {
                if (note.Tick == tick)
                {
                    return note;
                }
            }
            throw new ArgumentException($"tick {tick} のノートがありません。", nameof(tick));
        }

        private static Note Copy(Note note)
        {
            if (note is null || note.Effects is null)
            {
                throw new SongValidationException("note と effects は null にできません。");
            }
            return new Note
            {
                Tick = note.Tick,
                DurationTicks = note.DurationTicks,
                MidiNote = note.MidiNote,
                Volume = note.Volume,
                InstrumentId = note.InstrumentId,
                Effects = (NoteEffect[])note.Effects.Clone()
            };
        }

        private static int CompareTicks(Note left, Note right)
        {
            return left.Tick.CompareTo(right.Tick);
        }
    }
}
