using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Session
{
    /// <summary>候補ソングだけを編集し、バッチ全体を一履歴として保存する。</summary>
    public static class BatchOperationApplier
    {
        /// <summary>順番に操作し、途中の失敗ではファイル・ソング・履歴を変更しない。</summary>
        public static void Apply(EditSession session, IReadOnlyList<BatchOperation> operations, int? defaultTrack = null)
        {
            if (operations.Count == 0)
            {
                throw new ArgumentException("操作を一つ以上指定してください。", nameof(operations));
            }
            session.Change(song =>
            {
                foreach (BatchOperation operation in operations)
                {
                    ApplyOperation(song, operation, defaultTrack);
                    // 途中の不正状態を後続操作で覆い隠さず、通常編集と同じ制約を保つ。
                    SongValidator.Validate(song);
                }
            });
        }

        private static void ApplyOperation(Song song, BatchOperation operation, int? defaultTrack)
        {
            switch (operation.Kind)
            {
                case BatchOperationKind.AddNote:
                case BatchOperationKind.RemoveNote:
                case BatchOperationKind.MoveNote:
                case BatchOperationKind.ResizeNote:
                case BatchOperationKind.UpdateNote:
                    ApplyNote(song, Require(operation.Track ?? defaultTrack, "track"), operation);
                    return;
                case BatchOperationKind.AddInstrument:
                    song.Instruments.Add(RequireInstrument(operation));
                    return;
                case BatchOperationKind.UpdateInstrument:
                    Instrument replacement = RequireInstrument(operation);
                    song.Instruments[FindInstrument(song, replacement.Id)] = replacement;
                    return;
                case BatchOperationKind.RemoveInstrument:
                    RemoveInstrument(song, Require(operation.InstrumentId, "instrumentId"));
                    return;
                case BatchOperationKind.SetTempo:
                    song.TempoBpm = Require(operation.TempoBpm, "tempoBpm");
                    return;
                case BatchOperationKind.SetLength:
                    song.LengthTicks = Require(operation.LengthTicks, "lengthTicks");
                    return;
                case BatchOperationKind.SetLoopStart:
                    song.LoopStartTick = Require(operation.LoopStartTick, "loopStartTick");
                    return;
                case BatchOperationKind.SetTrackMuted:
                    EditSession.GetTrack(song, Require(operation.Track ?? defaultTrack, "track")).Muted = Require(operation.Muted, "muted");
                    return;
                case BatchOperationKind.SetTrackPan:
                    EditSession.GetTrack(song, Require(operation.Track ?? defaultTrack, "track")).Pan = Require(operation.Pan, "pan");
                    return;
                default:
                    throw new ArgumentException("操作 kind が未指定または未対応です。");
            }
        }

        private static void ApplyNote(Song song, int trackIndex, BatchOperation operation)
        {
            Track track = EditSession.GetTrack(song, trackIndex);
            int tick = Require(operation.Tick, "tick");
            if (operation.Kind == BatchOperationKind.AddNote)
            {
                Note added = new Note
                {
                    Tick = tick,
                    DurationTicks = Require(operation.DurationTicks, "durationTicks"),
                    MidiNote = Require(operation.MidiNote, "midiNote"),
                    Effects = operation.Effects ?? Array.Empty<NoteEffect>()
                };
                added.Volume = operation.Volume ?? added.Volume;
                added.InstrumentId = DefaultInstrumentResolver.Resolve(song, trackIndex, operation.InstrumentId);
                track.Notes.Add(added);
                track.Notes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
                return;
            }
            Note note = track.Notes.Find(candidate => candidate.Tick == tick)
                ?? throw new ArgumentException($"tick {tick} のノートがありません。");
            switch (operation.Kind)
            {
                case BatchOperationKind.RemoveNote:
                    track.Notes.Remove(note);
                    return;
                case BatchOperationKind.MoveNote:
                    note.Tick = Require(operation.ToTick, "toTick");
                    note.MidiNote = operation.MidiNote ?? note.MidiNote;
                    break;
                case BatchOperationKind.ResizeNote:
                    note.DurationTicks = Require(operation.DurationTicks, "durationTicks");
                    break;
                case BatchOperationKind.UpdateNote:
                    UpdateNote(note, operation);
                    break;
            }
            track.Notes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
        }

        private static void UpdateNote(Note note, BatchOperation operation)
        {
            note.Tick = operation.ToTick ?? note.Tick;
            note.DurationTicks = operation.DurationTicks ?? note.DurationTicks;
            note.MidiNote = operation.MidiNote ?? note.MidiNote;
            note.Volume = operation.Volume ?? note.Volume;
            note.InstrumentId = operation.InstrumentId ?? note.InstrumentId;
            note.Effects = operation.Effects ?? note.Effects;
        }

        private static Instrument RequireInstrument(BatchOperation operation)
        {
            return operation.Instrument ?? throw new ArgumentException("instrument が必要です。");
        }

        private static int FindInstrument(Song song, int instrumentId)
        {
            int index = song.Instruments.FindIndex(instrument => instrument.Id == instrumentId);
            if (index < 0)
            {
                throw new ArgumentException($"音色 ID {instrumentId} がありません。");
            }
            return index;
        }

        private static void RemoveInstrument(Song song, int instrumentId)
        {
            int index = FindInstrument(song, instrumentId);
            foreach (Track track in song.Tracks)
            {
                if (track.DefaultInstrumentId == instrumentId || track.Notes.Exists(note => note.InstrumentId == instrumentId))
                {
                    throw new InvalidOperationException($"音色 ID {instrumentId} はトラックの既定音色またはノートから参照されています。");
                }
            }
            song.Instruments.RemoveAt(index);
        }

        private static T Require<T>(T? value, string name) where T : struct
        {
            return value ?? throw new ArgumentException($"{name} が必要です。");
        }
    }
}
