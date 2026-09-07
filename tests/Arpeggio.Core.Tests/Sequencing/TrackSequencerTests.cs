using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sequencing;
using Xunit;

namespace Arpeggio.Core.Tests.Sequencing
{
    /// <summary>ノート境界、遅延、周回、バッファ間の編集反映を検証する。</summary>
    public sealed class TrackSequencerTests
    {
        private const int TempoBpm = 150;
        private const int SampleRate = 44100;
        private const int SongLengthTicks = 192;

        /// <summary>Delay は発音開始だけを遅らせ、元の終了位置で停止する。</summary>
        [Fact]
        public void TryGetEvent_DelayShortensSoundingDuration()
        {
            const int DelayTicks = 12;
            const int DurationTicks = 48;
            var note = new Note
            {
                DurationTicks = DurationTicks,
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, DelayTicks) }
            };
            var track = new Track { Notes = new List<Note> { note } };
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(TempoBpm, SampleRate);
            long onsetSample = clock.TickToSamples(DelayTicks);
            long endSample = clock.TickToSamples(DurationTicks);

            Assert.False(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out _));
            Assert.False(sequencer.TryGetEvent(onsetSample - 1, clock, SongLengthTicks, 0, 1, out _));
            Assert.True(sequencer.TryGetEvent(onsetSample, clock, SongLengthTicks, 0, 1, out NoteEvent onset));
            Assert.True(onset.IsNoteOn);
            Assert.Same(note, onset.Note);
            Assert.Equal(onsetSample, onset.PositionSamples);
            Assert.Equal(DurationTicks - DelayTicks, onset.DurationTicks);
            Assert.Equal(0.0, onset.ElapsedTicks, precision: 8);
            Assert.False(sequencer.TryGetEvent(endSample - 1, clock, SongLengthTicks, 0, 1, out _));
            Assert.True(sequencer.TryGetEvent(endSample, clock, SongLengthTicks, 0, 1, out NoteEvent ending));
            Assert.False(ending.IsNoteOn);
            Assert.Null(ending.Note);
        }

        /// <summary>連続するノートを同一サンプルで一つの交代イベントへまとめる。</summary>
        [Fact]
        public void TryGetEvent_AdjacentNotesSwitchAtRoundedSampleBoundary()
        {
            const int BoundaryTick = 1;
            var first = new Note { DurationTicks = BoundaryTick };
            var second = new Note { Tick = BoundaryTick, DurationTicks = 1, MidiNote = 64 };
            var track = new Track { Notes = new List<Note> { first, second } };
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(137, SampleRate);
            long boundarySample = clock.TickToSamples(BoundaryTick);

            Assert.True(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out NoteEvent initial));
            Assert.Same(first, initial.Note);
            Assert.False(sequencer.TryGetEvent(boundarySample - 1, clock, SongLengthTicks, 0, 1, out _));
            Assert.True(sequencer.TryGetEvent(boundarySample, clock, SongLengthTicks, 0, 1, out NoteEvent transition));
            Assert.True(transition.IsNoteOn);
            Assert.Same(second, transition.Note);
            Assert.Equal(boundarySample, transition.PositionSamples);
            Assert.False(sequencer.TryGetEvent(boundarySample, clock, SongLengthTicks, 0, 1, out _));
        }

        /// <summary>同一ノートがループ境界をまたいでも周回開始で再発音する。</summary>
        [Fact]
        public void TryGetEvent_LoopRetriggersSameNoteAndStopsAtBodyEnd()
        {
            const int LoopStartTick = 48;
            const int LoopCount = 3;
            var note = new Note { DurationTicks = SongLengthTicks };
            var track = new Track { Notes = new List<Note> { note } };
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(TempoBpm, SampleRate);
            int loopLengthTicks = SongLengthTicks - LoopStartTick;

            Assert.True(sequencer.TryGetEvent(0, clock, SongLengthTicks, LoopStartTick, LoopCount, out _));
            long firstLoopSample = clock.TickToSamples(SongLengthTicks);
            Assert.False(sequencer.TryGetEvent(firstLoopSample - 1, clock, SongLengthTicks, LoopStartTick, LoopCount, out _));
            Assert.True(sequencer.TryGetEvent(firstLoopSample, clock, SongLengthTicks, LoopStartTick, LoopCount, out NoteEvent firstLoop));
            Assert.Same(note, firstLoop.Note);
            Assert.Equal(LoopStartTick, firstLoop.ElapsedTicks);
            long secondLoopSample = clock.TickToSamples(SongLengthTicks + loopLengthTicks);
            Assert.True(sequencer.TryGetEvent(secondLoopSample, clock, SongLengthTicks, LoopStartTick, LoopCount, out NoteEvent secondLoop));
            Assert.Same(note, secondLoop.Note);
            Assert.Equal(LoopStartTick, secondLoop.ElapsedTicks);
            long endingSample = clock.TickToSamples(SongLengthTicks + (LoopCount - 1) * loopLengthTicks);
            Assert.True(sequencer.TryGetEvent(endingSample, clock, SongLengthTicks, LoopStartTick, LoopCount, out NoteEvent ending));
            Assert.False(ending.IsNoteOn);
            Assert.False(sequencer.TryGetEvent(endingSample + 1, clock, SongLengthTicks, LoopStartTick, LoopCount, out _));
        }

        /// <summary>編集されたリストは Refresh 以降の発音に反映される。</summary>
        [Fact]
        public void Refresh_ReplacesSnapshotAndRetriggersEditedNote()
        {
            var original = new Note { DurationTicks = SongLengthTicks };
            var replacement = new Note { DurationTicks = SongLengthTicks, MidiNote = 72 };
            var track = new Track { Notes = new List<Note> { original } };
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(TempoBpm, SampleRate);

            Assert.True(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out _));
            track.Notes = new List<Note> { replacement };
            Assert.False(sequencer.TryGetEvent(1, clock, SongLengthTicks, 0, 1, out _));
            sequencer.Refresh();
            Assert.True(sequencer.TryGetEvent(2, clock, SongLengthTicks, 0, 1, out NoteEvent replacementEvent));
            Assert.Same(replacement, replacementEvent.Note);
            track.Notes = new List<Note>();
            sequencer.Refresh();
            Assert.True(sequencer.TryGetEvent(3, clock, SongLengthTicks, 0, 1, out NoteEvent removalEvent));
            Assert.False(removalEvent.IsNoteOn);
        }

        /// <summary>Reset 後は同じ位置でも発音イベントが再度返る。</summary>
        [Fact]
        public void Reset_AllowsCurrentNoteToBeTriggeredAgain()
        {
            var track = new Track { Notes = new List<Note> { new Note() } };
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(TempoBpm, SampleRate);

            Assert.True(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out _));
            Assert.False(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out _));
            sequencer.Reset();
            Assert.True(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out NoteEvent restarted));
            Assert.True(restarted.IsNoteOn);
        }

        /// <summary>ミュートはミキサー段の責務なので、シーケンサはミュート中もノートを止めない（SPC700 のピッチモジュレーション源を保つため）。</summary>
        [Fact]
        public void TryGetEvent_MutingDoesNotStopSequencing()
        {
            var note = new Note { DurationTicks = SongLengthTicks };
            var track = new Track { Notes = new List<Note> { note } };
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(TempoBpm, SampleRate);

            Assert.True(sequencer.TryGetEvent(0, clock, SongLengthTicks, 0, 1, out NoteEvent started));
            Assert.Same(note, started.Note);
            track.Muted = true;
            Assert.False(sequencer.TryGetEvent(1, clock, SongLengthTicks, 0, 1, out _));
            Assert.True(sequencer.TryGetActive(1, out Note? active, out _));
            Assert.Same(note, active);
            track.Muted = false;
            Assert.False(sequencer.TryGetEvent(2, clock, SongLengthTicks, 0, 1, out _));
        }
    }
}
