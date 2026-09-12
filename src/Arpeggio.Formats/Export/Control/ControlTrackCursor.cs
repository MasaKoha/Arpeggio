using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sequencing;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Formats.Export.Control
{
    /// <summary>一トラックの境界・シーケンサー・マクロ状態を同期させる。</summary>
    internal sealed class ControlTrackCursor : IDisposable
    {
        private readonly Song _song;
        private readonly Track _track;
        private readonly int _trackIndex;
        private readonly int _loops;
        private readonly TickClock _clock;
        private readonly TrackSequencer _sequencer;
        private readonly VoiceModulation _modulation = new VoiceModulation();
        private readonly IReadOnlyDictionary<int, Instrument> _instruments;
        private readonly Dictionary<Note, ControlNote> _notes = new Dictionary<Note, ControlNote>();
        private readonly IEnumerator<long> _boundaries;
        private ControlNote? _activeNote;
        private long _frame;

        internal ControlTrackCursor(Song song, int trackIndex, int loops, TickClock clock,
            IReadOnlyDictionary<int, Instrument> instruments, ConversionReport report)
        {
            _song = song;
            _track = song.Tracks[trackIndex];
            _trackIndex = trackIndex;
            _loops = loops;
            _clock = clock;
            _instruments = instruments;
            _sequencer = new TrackSequencer(_track);
            for (int noteIndex = 0; noteIndex < _track.Notes.Count; noteIndex++)
            {
                Note note = _track.Notes[noteIndex];
                _notes.Add(note, new ControlNote(note, noteIndex));
            }
            _boundaries = ControlBoundaries.Enumerate(song, trackIndex, loops, clock, report).GetEnumerator();
            NextBoundary = 0;
        }

        internal long NextBoundary { get; private set; }
        internal ControlEvent? PendingStop { get; private set; }
        internal ControlEvent? PendingState { get; private set; }

        internal void Prepare(long positionSamples, bool isFrameBoundary, bool isEnd)
        {
            PendingStop = null;
            PendingState = null;
            while (NextBoundary <= positionSamples)
            {
                NextBoundary = _boundaries.MoveNext() ? _boundaries.Current : long.MaxValue;
            }
            if (isFrameBoundary && _activeNote != null)
            {
                _modulation.AdvanceFrame();
                _frame++;
            }
            if (isEnd)
            {
                PendingStop = CreateStop(positionSamples);
                _activeNote = null;
                return;
            }
            if (!_track.Muted && _sequencer.TryGetEvent(positionSamples, _clock, _song.LengthTicks,
                _song.LoopStartTick, _loops, out NoteEvent noteEvent))
            {
                ApplyTransition(positionSamples, noteEvent);
            }
            else if (isFrameBoundary && _activeNote != null)
            {
                PendingState = CreateState(positionSamples, ControlEventKind.Update);
            }
        }

        /// <summary>遅延列挙の状態を破棄する。構築側が finally で必ず呼ぶ。</summary>
        public void Dispose() => _boundaries.Dispose();

        private void ApplyTransition(long positionSamples, NoteEvent noteEvent)
        {
            if (_activeNote != null)
            {
                PendingStop = CreateStop(positionSamples);
            }
            _activeNote = null;
            if (noteEvent.Note is not Note note)
            {
                return;
            }
            _activeNote = _notes[note];
            _frame = 0;
            _modulation.Start(note.MidiNote, note.Volume, note.Effects);
            Configure(_instruments[note.InstrumentId]);
            double remainingTicks = Math.Max(0, noteEvent.DurationTicks - noteEvent.ElapsedTicks);
            double seconds = _clock.TickToSamples(remainingTicks) / (double)ConversionLimits.ControlSampleRate;
            _modulation.SetDuration(seconds);
            PendingState = CreateState(positionSamples, ControlEventKind.NoteOn);
        }

        private void Configure(Instrument instrument)
        {
            switch (instrument)
            {
                case NesPulseInstrument pulse:
                    _modulation.Configure(pulse.VolumeMacro, pulse.ArpeggioMacro, pulse.PitchMacro, pulse.DutyMacro, (int)pulse.Duty);
                    break;
                case NesTriangleInstrument triangle:
                    _modulation.Configure(null, triangle.ArpeggioMacro, triangle.PitchMacro);
                    break;
                case NesNoiseInstrument noise:
                    _modulation.Configure(noise.VolumeMacro, null, noise.PitchMacro);
                    break;
                case GbPulseInstrument pulse:
                    _modulation.Configure(pulse.VolumeMacro, pulse.ArpeggioMacro, pulse.PitchMacro, pulse.DutyMacro, (int)pulse.Duty);
                    break;
                case GbWaveInstrument wave:
                    _modulation.Configure(null, wave.ArpeggioMacro, wave.PitchMacro);
                    break;
                case GbNoiseInstrument noise:
                    _modulation.Configure(noise.VolumeMacro, null, noise.PitchMacro);
                    break;
                case NesDpcmInstrument:
                    _modulation.Configure(null, null, null);
                    break;
                default:
                    throw new InvalidOperationException("検証済み制御列に非対応の音色が含まれています。");
            }
        }

        private ControlEvent CreateStop(long positionSamples)
            => new ControlEvent(positionSamples, _trackIndex, ControlEventKind.NoteOff, _activeNote, _frame, 0, 0, 0);

        private ControlEvent CreateState(long positionSamples, ControlEventKind kind)
            => new ControlEvent(positionSamples, _trackIndex, kind, _activeNote, _frame,
                _modulation.MidiNote, _modulation.Volume, _modulation.Duty);
    }
}
