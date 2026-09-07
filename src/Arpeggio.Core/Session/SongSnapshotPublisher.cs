using System;
using System.Collections.Generic;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Session
{
    /// <summary>編集済みスナップショットから差分だけを公開し、再生中の未変更発音を維持する。</summary>
    internal static class SongSnapshotPublisher
    {
        internal static void Apply(Song current, Song candidate)
        {
            current.Version = candidate.Version;
            current.Title = candidate.Title;
            current.Chip = candidate.Chip;
            current.TempoBpm = candidate.TempoBpm;
            current.TicksPerBeat = candidate.TicksPerBeat;
            current.LengthTicks = candidate.LengthTicks;
            current.LoopStartTick = candidate.LoopStartTick;
            current.Instruments = PreserveInstruments(current.Instruments, candidate.Instruments);
            current.SnesEcho = candidate.SnesEcho;
            if (current.Tracks.Count != candidate.Tracks.Count)
            {
                current.Tracks = candidate.Tracks;
                return;
            }
            for (int index = 0; index < current.Tracks.Count; index++)
            {
                ApplyTrack(current.Tracks[index], candidate.Tracks[index]);
            }
        }

        private static List<Instrument> PreserveInstruments(List<Instrument> current, List<Instrument> candidate)
        {
            var instrumentsById = new Dictionary<int, Instrument>();
            foreach (Instrument instrument in current)
            {
                instrumentsById.Add(instrument.Id, instrument);
            }
            bool isUnchanged = current.Count == candidate.Count;
            for (int index = 0; index < candidate.Count; index++)
            {
                Instrument replacement = candidate[index];
                if (instrumentsById.TryGetValue(replacement.Id, out Instrument? original) &&
                    JsonSerializer.Serialize<Instrument>(original) == JsonSerializer.Serialize<Instrument>(replacement))
                {
                    candidate[index] = original;
                }
                isUnchanged = isUnchanged && ReferenceEquals(current[index], candidate[index]);
            }
            return isUnchanged ? current : candidate;
        }

        private static void ApplyTrack(Track current, Track candidate)
        {
            current.Channel = candidate.Channel;
            current.ChannelIndex = candidate.ChannelIndex;
            current.Name = candidate.Name;
            current.Muted = candidate.Muted;
            current.Pan = candidate.Pan;
            // 公開済みリストを変更せず、無関係なノート編集による再発音も防ぐ。
            current.Notes = PreserveNotes(current.Notes, candidate.Notes);
        }

        private static List<Note> PreserveNotes(List<Note> current, List<Note> candidate)
        {
            var notesByTick = new Dictionary<int, Note>();
            foreach (Note note in current)
            {
                notesByTick.Add(note.Tick, note);
            }
            bool isUnchanged = current.Count == candidate.Count;
            for (int index = 0; index < candidate.Count; index++)
            {
                Note replacement = candidate[index];
                if (notesByTick.TryGetValue(replacement.Tick, out Note? original) && NotesEqual(original, replacement))
                {
                    candidate[index] = original;
                }
                isUnchanged = isUnchanged && ReferenceEquals(current[index], candidate[index]);
            }
            return isUnchanged ? current : candidate;
        }

        private static bool NotesEqual(Note original, Note replacement)
        {
            return original.Tick == replacement.Tick && original.DurationTicks == replacement.DurationTicks &&
                original.MidiNote == replacement.MidiNote && original.Volume == replacement.Volume &&
                original.InstrumentId == replacement.InstrumentId &&
                original.Effects.AsSpan().SequenceEqual(replacement.Effects);
        }
    }
}
