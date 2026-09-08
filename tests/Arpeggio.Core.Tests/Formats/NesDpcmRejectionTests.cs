using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>DPCM の拒否条件と、予約音色・ミュートの許可条件を検証する。</summary>
    public sealed class NesDpcmRejectionTests
    {
        private const int DpcmTrack = 4;
        private const int DpcmInstrument = 2;
        private const int FrameTicks = 2;
        private const int Status = 0x4015;
        private const int DpcmEnable = 0x10;
        private const int DmcControl = 0x4010;
        private const int DmcOutput = 0x4011;
        private const int DmcAddress = 0x4012;
        private const int DmcLength = 0x4013;

        /// <summary>遅延発音・音量ゼロも DPCM ノートとして拒否し、strict に依存しない。</summary>
        [Theory]
        [InlineData(false, 0, 15)]
        [InlineData(true, 0, 15)]
        [InlineData(false, 1, 0)]
        [InlineData(true, 1, 0)]
        public void AnyUnmutedDpcmNoteIsAnError(bool strict, int delay, int volume)
        {
            Song song = CreateSong();
            song.Tracks[DpcmTrack].Notes.Add(new Note
            {
                Tick = FrameTicks, DurationTicks = FrameTicks, Volume = volume, InstrumentId = DpcmInstrument,
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, delay) }
            });
            ControlTimelineResult control = CreateControl(song, strict);
            Assert.NotNull(control.Timeline);
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            ConversionDiagnostic error = Assert.Single(control.Report.Errors);
            Assert.Equal("UnsupportedDpcm", error.Code);
            Assert.Equal(DpcmTrack, error.SourceTrack);
            Assert.Equal(DpcmTrack, error.OutputTrack);
            Assert.Equal(0L, error.SourceEvent);
            Assert.Equal(FrameTicks, error.SourceTick);
            Assert.False(control.Report.CanWrite);
            Assert.Equal(0L, control.Report.Statistics["registerWrites"]);
        }

        /// <summary>未使用予約音色と空 DPCM トラックは許可し、DMC を一度も開始しない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EmptyDpcmTrackAndUnusedInstrumentAreAllowed(bool includeReservedInstrument)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: FrameTicks);
            if (includeReservedInstrument)
            {
                song.Instruments.Add(new NesDpcmInstrument { Id = DpcmInstrument });
            }
            ControlTimelineResult control = CreateControl(song, strict: true);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Empty(control.Report.Errors);
            Assert.Empty(control.Report.Warnings);
            AssertDpcmNeverStarts(timeline);
        }

        /// <summary>DPCM のミュートをスナップショットに固定し、後の解除でも拒否や発音を追加しない。</summary>
        [Fact]
        public void MutedDpcmIsAllowedAndSnapshotDoesNotFollowLaterUnmute()
        {
            Song song = CreateSong();
            song.Tracks[DpcmTrack].Muted = true;
            song.Tracks[DpcmTrack].Pan = 1;
            song.Tracks[DpcmTrack].Notes.Add(new Note { DurationTicks = FrameTicks, InstrumentId = DpcmInstrument });
            ControlTimelineResult control = CreateControl(song, strict: true);
            Assert.NotNull(control.Timeline);
            song.Tracks[DpcmTrack].Muted = false;
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Empty(control.Report.Errors);
            Assert.Empty(control.Report.Warnings);
            AssertDpcmNeverStarts(timeline);
        }

        /// <summary>元ノート二件を一件ずつ診断し、二周への展開や元 Song の編集で診断件数を変えない。</summary>
        [Fact]
        public void RepeatedLoopsRejectOriginalNotesOnceAndIgnoreLaterSourceEdits()
        {
            Song song = CreateSong();
            song.Tracks[DpcmTrack].Notes.Add(new Note { DurationTicks = FrameTicks, InstrumentId = DpcmInstrument });
            song.Tracks[DpcmTrack].Notes.Add(new Note { Tick = FrameTicks, DurationTicks = FrameTicks, InstrumentId = DpcmInstrument });
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Nsf, Loops = 2 });
            Assert.NotNull(control.Timeline);
            song.Tracks[DpcmTrack].Notes.Clear();
            song.Tracks[DpcmTrack].Muted = true;
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Equal(2L, control.Report.ErrorCount);
            Assert.Equal(new long?[] { 0, 1 }, control.Report.Errors.Select(error => error.SourceEvent));
            Assert.All(control.Report.Errors, error => Assert.Equal(1L, error.OccurrenceCount));
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, diagnosticDetailLimit: 0);
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, report));
            Assert.Empty(report.Errors);
            Assert.Equal(2L, report.DroppedErrorCount);
            Assert.False(report.CanWrite);
        }

        private static Song CreateSong()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: FrameTicks * 2);
            song.Instruments.Add(new NesDpcmInstrument { Id = DpcmInstrument });
            return song;
        }

        private static ControlTimelineResult CreateControl(Song song, bool strict)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Strict = strict });

        private static void AssertDpcmNeverStarts(RegisterTimeline timeline)
        {
            Assert.All(timeline.Writes.Where(write => write.Address == Status), write => Assert.Equal(0, write.Value & DpcmEnable));
            Assert.DoesNotContain(timeline.Writes, write => write.Address == DmcAddress || write.Address == DmcLength);
            Assert.Equal(0, Assert.Single(timeline.Writes, write => write.Address == DmcControl).Value);
            Assert.Equal(0, Assert.Single(timeline.Writes, write => write.Address == DmcOutput).Value);
        }
    }
}
