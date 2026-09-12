using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sequencing;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;

namespace Arpeggio.Core.Tests.Formats.Export.Control
{
    /// <summary>サンプルを生成しない制御列の端点・グローバルフレーム・有限周回を検証する。</summary>
    public sealed class ControlTimelineTests
    {
        /// <summary>Delay 後に発音し、実際の発音期間をスライド分母にして元終端で停止する。</summary>
        [Fact]
        public void DelayPreservesOriginalEndAndRemainingSlideDuration()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 8);
            song.Tracks[0].Notes.Add(new Note
            {
                Tick = 1, DurationTicks = 5,
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 1), new NoteEffect(NoteEffectKind.PitchSlide, 6) }
            });
            ControlTimeline timeline = Create(song);
            ControlEvent onset = Assert.Single(timeline.Events, control => control.Kind == ControlEventKind.NoteOn);
            Assert.Equal(735, onset.PositionSamples);
            Assert.Equal(60.0, onset.MidiNote);
            ControlEvent update = Assert.Single(timeline.Events, control => control.Kind == ControlEventKind.Update);
            Assert.Equal(1470, update.PositionSamples);
            Assert.Equal(63.0, update.MidiNote);
            Assert.Contains(timeline.Events, control => control.TrackIndex == 0 && control.Kind == ControlEventKind.NoteOff && control.PositionSamples == 2205);
            Assert.Equal(2940, timeline.EndSamples);
        }

        /// <summary>フレーム途中の On も次の曲全体フレームで進み、半サンプル時刻はゼロから遠ざける。</summary>
        [Fact]
        public void MidFrameOnUsesGlobalBoundaryAndAwayFromZeroRounding()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 4);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 4, 7 } };
            song.Tracks[0].Notes.Add(new Note { Tick = 1, DurationTicks = 3 });
            ControlTimeline timeline = Create(song);
            ControlEvent onset = Assert.Single(timeline.Events, control => control.Kind == ControlEventKind.NoteOn);
            Assert.Equal(368, onset.PositionSamples);
            Assert.Equal(60.0, onset.MidiNote);
            ControlEvent update = Assert.Single(timeline.Events, control => control.Kind == ControlEventKind.Update);
            Assert.Equal(735, update.PositionSamples);
            Assert.Equal(64.0, update.MidiNote);
            Assert.Equal(1, update.Frame);
            Assert.DoesNotContain(timeline.Events, control => control.PositionSamples == 1470 && control.Kind == ControlEventKind.Update);
        }

        /// <summary>一フレームより短い音を保持し、更新前に停止する。</summary>
        [Fact]
        public void ShortNoteKeepsItsGateWithoutAdvancingEffects()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 1, Effects = new[] { new NoteEffect(NoteEffectKind.PitchSlide, 12) } });
            ControlTimeline timeline = Create(song);
            Assert.Equal(0, Assert.Single(timeline.Events, control => control.Kind == ControlEventKind.NoteOn).PositionSamples);
            Assert.Contains(timeline.Events, control => control.TrackIndex == 0 && control.Kind == ControlEventKind.NoteOff && control.PositionSamples == 368);
            Assert.DoesNotContain(timeline.Events, control => control.Kind == ControlEventKind.Update);
        }

        /// <summary>フレームと交代が重なると旧値を捨て、全 Off の後にトラック順で On を出す。</summary>
        [Fact]
        public void SimultaneousReplacementSuppressesOldUpdatesAndOrdersAllStopsFirst()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 6);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 7 }, LoopIndex = 0 };
            for (int trackIndex = 0; trackIndex < 2; trackIndex++)
            {
                song.Tracks[trackIndex].Notes.Add(new Note { DurationTicks = 2 });
                song.Tracks[trackIndex].Notes.Add(new Note { Tick = 2, DurationTicks = 2, MidiNote = 72 });
            }
            ControlEvent[] simultaneous = Create(song).Events.Where(control => control.PositionSamples == 735).ToArray();
            Assert.Equal(new[] { ControlEventKind.NoteOff, ControlEventKind.NoteOff, ControlEventKind.NoteOn, ControlEventKind.NoteOn }, simultaneous.Select(control => control.Kind));
            Assert.Equal(new[] { 0, 1, 0, 1 }, simultaneous.Select(control => control.TrackIndex));
            Assert.All(simultaneous.Where(control => control.Kind == ControlEventKind.NoteOn), control =>
            {
                Assert.Equal(72.0, control.MidiNote);
                Assert.Equal(0, control.Frame);
            });
        }

        /// <summary>二周目はループ開始をまたぐ同じノートを残り期間で再発音し、フレーム位相を保持する。</summary>
        [Fact]
        public void FiniteSecondCycleRetriggersAndKeepsGlobalFramePhase()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 5);
            song.LoopStartTick = 2;
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 5, Effects = new[] { new NoteEffect(NoteEffectKind.PitchSlide, 6) } });
            ControlTimeline timeline = Create(song, loops: 2);
            Assert.Equal(2940, timeline.EndSamples);
            ControlEvent[] onsets = timeline.Events.Where(control => control.Kind == ControlEventKind.NoteOn).ToArray();
            Assert.Equal(new long[] { 0, 1838 }, onsets.Select(control => control.PositionSamples));
            Assert.All(onsets, control => Assert.Equal(60.0, control.MidiNote));
            Assert.Same(onsets[0].Note, onsets[1].Note);
            ControlEvent update = Assert.Single(timeline.Events, control => control.PositionSamples == 2205 && control.Kind == ControlEventKind.Update);
            Assert.Equal(1, update.Frame);
            Assert.Equal(60 + 6 * 735.0 / 1102, update.MidiNote, precision: 10);
            Assert.Equal(song.Tracks.Count, timeline.Events.Count(control => control.PositionSamples == timeline.EndSamples));
            Assert.All(timeline.Events.Where(control => control.PositionSamples == timeline.EndSamples), control => Assert.Equal(ControlEventKind.NoteOff, control.Kind));
        }

        /// <summary>ループ開始時点が Delay の途中なら遅延後まで再発音しない。</summary>
        [Fact]
        public void LoopStartingDuringDelayWaitsForDelayedOnset()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 5);
            song.LoopStartTick = 2;
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 5, Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 3) } });
            ControlTimeline timeline = Create(song, loops: 2);
            Assert.Equal(new long[] { 1103, 2205 }, timeline.Events.Where(control => control.Kind == ControlEventKind.NoteOn).Select(control => control.PositionSamples));
            Assert.DoesNotContain(timeline.Events, control => control.PositionSamples == 1838 && control.Kind == ControlEventKind.NoteOn);
        }

        /// <summary>既存シーケンサーのサンプル単位観測と発音・停止列が一致する。</summary>
        [Theory]
        [InlineData(137)]
        [InlineData(150)]
        [InlineData(1000)]
        public void GatesMatchExistingSequencerAtEverySample(int tempoBpm)
        {
            const int Loops = 2;
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm, lengthTicks: 7);
            song.LoopStartTick = 1;
            Track track = song.Tracks[0];
            track.Notes.Add(new Note { DurationTicks = 2, Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 1) } });
            track.Notes.Add(new Note { Tick = 2, DurationTicks = 1 });
            track.Notes.Add(new Note { Tick = 4, DurationTicks = 3 });
            ControlTimeline timeline = Create(song, Loops);
            var sequencer = new TrackSequencer(track);
            var clock = new TickClock(tempoBpm, 44100);
            var expected = new List<(long Position, ControlEventKind Kind, int? Tick)>();
            Note? activeNote = null;
            for (long sample = 0; sample <= timeline.EndSamples; sample++)
            {
                if (!sequencer.TryGetEvent(sample, clock, song.LengthTicks, song.LoopStartTick, Loops, out NoteEvent noteEvent))
                {
                    continue;
                }
                if (activeNote != null)
                {
                    expected.Add((sample, ControlEventKind.NoteOff, activeNote.Tick));
                }
                activeNote = noteEvent.Note;
                if (activeNote != null)
                {
                    expected.Add((sample, ControlEventKind.NoteOn, activeNote.Tick));
                }
            }
            var actual = timeline.Events.Where(control => control.TrackIndex == 0 && control.Kind != ControlEventKind.Update)
                .Select(control => (control.PositionSamples, control.Kind, control.Note?.Tick)).ToArray();
            Assert.Equal(expected.ToArray(), actual);
        }

        /// <summary>極端なテンポで消える正長ノートを、曲全体がゼロサンプルの場合も拒否する。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(100000)]
        public void CollapsedPositiveGateReturnsErrorWithoutPartialTimeline(int lengthTicks)
        {
            Song song = SongFactory.Create(ChipKind.Nes, int.MaxValue, lengthTicks);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 1 });
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.Null(result.Timeline);
            Assert.False(result.Report.CanWrite);
            ConversionDiagnostic diagnostic = Assert.Single(result.Report.Errors);
            Assert.Equal("ControlEventCollision", diagnostic.Code);
            Assert.Equal(0, diagnostic.SourceTrack);
            Assert.Equal(0, diagnostic.SourceTick);
        }

        /// <summary>ミュートは変換の固定停止状態であり、ノート更新を出さない。</summary>
        [Fact]
        public void MutedTrackRemainsStoppedAndAllChannelsStopAtEnd()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 4);
            song.Tracks[0].Muted = true;
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 4 });
            ControlTimeline timeline = Create(song);
            Assert.Equal(song.Tracks.Count, timeline.Events.Count);
            Assert.All(timeline.Events, control =>
            {
                Assert.Equal(timeline.EndSamples, control.PositionSamples);
                Assert.Equal(ControlEventKind.NoteOff, control.Kind);
            });
        }

        /// <summary>空の一秒曲は 61 境界だけを走査し、44100 回の描画経路を使わない。</summary>
        [Fact]
        public void EmptySongVisitsOnlyFrameBoundaries()
        {
            Song song = SongFactory.Create(ChipKind.GameBoy, tempoBpm: 60, lengthTicks: 48);
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Strict = true });
            Assert.NotNull(result.Timeline);
            Assert.True(result.Report.CanWrite);
            Assert.Equal(61, result.Report.Statistics["controlBoundaries"]);
            Assert.Equal(4, result.Timeline.Events.Count);
            Assert.Equal(44100, result.Timeline.EndSamples);
            Assert.Single(result.Report.Limitations);
        }

        /// <summary>元の不正ドキュメントは既存の入力例外で拒否する。</summary>
        [Fact]
        public void InvalidSongKeepsExistingValidationException()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Version = 2;
            Assert.Throws<SongValidationException>(() => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm }));
        }

        private static ControlTimeline Create(Song song, int loops = 1)
        {
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.True(result.Report.CanWrite);
            Assert.NotNull(result.Timeline);
            return result.Timeline;
        }
    }
}
