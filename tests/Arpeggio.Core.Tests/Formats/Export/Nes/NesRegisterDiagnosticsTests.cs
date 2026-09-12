using System.Globalization;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nes;

namespace Arpeggio.Core.Tests.Formats.Export.Nes
{
    /// <summary>NES 連続音域のクランプ、strict、DPCM 拒否を検証する。</summary>
    public sealed class NesRegisterDiagnosticsTests
    {
        private const int PulseOneTrack = 0;
        private const int PulseTwoTrack = 1;
        private const int TriangleTrack = 2;
        private const int NoiseTrack = 3;
        private const int DpcmTrack = 4;
        private const int TriangleInstrument = 2;
        private const int ConcertNote = 69;
        private const int FrameTicks = 2;
        private const int FrameSamples = 735;
        private const int PulseOneLow = 0x4002;
        private const int PulseOneHigh = 0x4003;
        private const int PulseTwoLow = 0x4006;
        private const int PulseTwoHigh = 0x4007;
        private const int TriangleLow = 0x400A;
        private const int TriangleHigh = 0x400B;
        private const int TimerHighMask = 7;
        private const int TimerHighShift = 8;
        private const int Status = 0x4015;

        /// <summary>Pulse 二声と Triangle の上下端を timer 8〜2047 に制限し、元位置つきの警告を返す。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, 0, 2047)]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, 127, 8)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh, 0, 2047)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh, 127, 8)]
        [InlineData(TriangleTrack, TriangleLow, TriangleHigh, 0, 2047)]
        [InlineData(TriangleTrack, TriangleLow, TriangleHigh, 127, 8)]
        public void OutOfRangePitchClampsTimerAndReportsSource(int trackIndex, int lowAddress, int highAddress,
            int midiNote, int expectedTimer)
        {
            Song song = CreateSong(trackIndex, midiNote);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.True(control.Report.CanWrite);
            int low = Assert.Single(timeline.Writes, write => write.PositionSamples == 0 && write.Address == lowAddress).Value;
            int high = Assert.Single(timeline.Writes, write => write.PositionSamples == 0 && write.Address == highAddress).Value;
            Assert.Equal(expectedTimer, low | ((high & TimerHighMask) << TimerHighShift));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PitchClamped", warning.Code);
            Assert.Equal(trackIndex, warning.SourceTrack);
            Assert.Equal(trackIndex, warning.OutputTrack);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.Equal(0L, warning.SourceTick);
            Assert.Equal(2L, warning.OccurrenceCount);
            Assert.Equal(midiNote.ToString(CultureInfo.InvariantCulture), warning.Original);
            Assert.NotNull(warning.Converted);
            double converted = double.Parse(warning.Converted, CultureInfo.InvariantCulture);
            ChannelKind channel = song.Tracks[trackIndex].Channel;
            Assert.Equal(PitchTable.ClampMidiNote(ChipKind.Nes, channel, midiNote), converted);
            Assert.Equal(2L, control.Report.WarningCountsByCode["PitchClamped"]);
        }

        /// <summary>通常の周期量子化と MIDI 往復誤差をクランプ警告へ混ぜない。</summary>
        [Theory]
        [InlineData(57)]
        [InlineData(60)]
        [InlineData(69)]
        [InlineData(72)]
        public void InRangePitchDoesNotWarnAboutOrdinaryQuantization(int midiNote)
        {
            ControlTimelineResult control = CreateControl(CreateSong(PulseOneTrack, midiNote));
            Assert.NotNull(control.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>変調中の複数クランプを元ノートへ集約し、最大の半音誤差を残す。</summary>
        [Fact]
        public void ModulatedClampsAggregateAtOriginalNoteWithMaximumError()
        {
            const int FirstOffset = -100;
            const int SecondOffset = -200;
            Song song = CreateSong(PulseOneTrack, ConcertNote);
            song.LengthTicks = FrameTicks * 4;
            Note note = song.Tracks[PulseOneTrack].Notes[0];
            note.Tick = FrameTicks;
            note.DurationTicks = FrameTicks * 3;
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).ArpeggioMacro = new Macro
            {
                Values = new[] { 0, FirstOffset, SecondOffset }
            };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings, diagnostic => diagnostic.Code == "PitchClamped");
            ConversionDiagnostic phaseWarning = Assert.Single(control.Report.Warnings, diagnostic => diagnostic.Code == "PulsePhaseRestarted");
            Assert.Equal(1L, phaseWarning.OccurrenceCount);
            Assert.Equal(2, control.Report.Warnings.Count);
            Assert.Equal(FrameTicks, warning.SourceTick);
            Assert.Equal(2L, warning.OccurrenceCount);
            Assert.Equal((ConcertNote + SecondOffset).ToString(CultureInfo.InvariantCulture), warning.Original);
            double lowerBound = PitchTable.ClampMidiNote(ChipKind.Nes, ChannelKind.Pulse, ConcertNote + SecondOffset);
            Assert.NotNull(warning.MaximumError);
            Assert.Equal(lowerBound - (ConcertNote + SecondOffset), warning.MaximumError.Value);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples == FrameSamples * 3);
        }

        /// <summary>strict は明細保持ゼロでも全警告数で拒否し、部分列を公開しない。</summary>
        [Fact]
        public void StrictRejectsClampingEvenWhenDetailsAreNotRetained()
        {
            ControlTimelineResult control = CreateControl(CreateSong(PulseOneTrack, midiNote: 0));
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, report));
            Assert.False(report.CanWrite);
            Assert.Empty(report.Warnings);
            Assert.Equal(2L, report.WarningCount);
            Assert.Equal(2L, report.DroppedWarningCount);
        }

        /// <summary>ミュートした低音は発音も音程警告も生じない。</summary>
        [Fact]
        public void MutedPulseDoesNotEnableOrReportPitchClamping()
        {
            Song song = CreateSong(PulseOneTrack, midiNote: 0);
            song.Tracks[PulseOneTrack].Muted = true;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Empty(control.Report.Warnings);
            Assert.All(timeline.Writes.Where(write => write.Address == Status), write => Assert.Equal(0, write.Value));
        }

        /// <summary>Noise と同時に DPCM が存在しても部分成功にせず、DPCM の元位置を報告する。</summary>
        [Fact]
        public void NoiseAndDpcmTogetherReturnDpcmError()
        {
            const int NoiseInstrument = 3;
            const int DpcmInstrument = 4;
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: FrameTicks * 2);
            song.Instruments.Add(new NesNoiseInstrument { Id = NoiseInstrument });
            song.Instruments.Add(new NesDpcmInstrument { Id = DpcmInstrument });
            song.Tracks[NoiseTrack].Notes.Add(new Note { DurationTicks = FrameTicks, InstrumentId = NoiseInstrument });
            song.Tracks[DpcmTrack].Notes.Add(new Note { DurationTicks = FrameTicks, InstrumentId = DpcmInstrument });
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.Null(timeline);
            ConversionDiagnostic error = Assert.Single(control.Report.Errors);
            Assert.Equal("UnsupportedDpcm", error.Code);
            Assert.Equal(DpcmTrack, error.SourceTrack);
            Assert.Equal(0L, error.SourceEvent);
            Assert.Equal(0L, error.SourceTick);
        }

        /// <summary>GB 制御列を NES レジスタへ誤変換しない。</summary>
        [Fact]
        public void NonNesTimelineReturnsUnsupportedChipError()
        {
            ControlTimelineResult control = CreateControl(SongFactory.Create(ChipKind.GameBoy));
            Assert.NotNull(control.Timeline);
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Equal("UnsupportedChip", Assert.Single(control.Report.Errors).Code);
        }

        private static Song CreateSong(int trackIndex, int midiNote)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: FrameTicks * 2);
            song.Instruments.Add(new NesTriangleInstrument { Id = TriangleInstrument });
            song.Tracks[trackIndex].Notes.Add(new Note
            {
                DurationTicks = song.LengthTicks, MidiNote = midiNote,
                InstrumentId = trackIndex == TriangleTrack ? TriangleInstrument : 1
            });
            return song;
        }

        private static ControlTimelineResult CreateControl(Song song)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
    }
}
