using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>PLAY 量子化を元の制御列と固定レジスタ値で検証する。</summary>
    public sealed class NsfFrameCompilerTests
    {
        /// <summary>半フレーム境界の両側と長時間の絶対時刻を整数式で丸める。</summary>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(366, 0)]
        [InlineData(367, 1)]
        [InlineData(735, 1)]
        [InlineData(44100, 60)]
        [InlineData(79380000, 108180)]
        public void AbsoluteSamplesHaveFixedPlayFrames(long samples, long expectedFrame)
        {
            Assert.Equal(expectedFrame, NsfTiming.Quantize(samples));
        }

        /// <summary>全時刻を絶対位置から量子化し、周回後も最大誤差は半 PLAY 以下となる。</summary>
        [Fact]
        public void TimingErrorAndLoopOnsetsUseAbsolutePositions()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 137, lengthTicks: 192);
            song.LoopStartTick = 48;
            song.Tracks[0].Notes.Add(new Note { Tick = 49, DurationTicks = 100, MidiNote = 69, InstrumentId = 1 });
            ControlTimelineResult source = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Nsf, Loops = 3 });
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? result = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(result);
            long[] expected = source.Timeline.Events.Where(control => control.TrackIndex == 0 && control.Kind == ControlEventKind.NoteOn)
                .Select(control => (long)Math.Round(control.PositionSamples * 1000000.0 / (44100 * 16639.0), MidpointRounding.AwayFromZero)).ToArray();
            Assert.Equal(expected, result.Writes.Where(write => write.Address == 0x4003).Select(write => write.Frame));
            double expectedMaximum = source.Timeline.Events.Where(control => control.TrackIndex == 0 && control.Note is not null)
                .Max(control => Math.Abs(Math.Round(control.PositionSamples * 1000000.0 / (44100 * 16639.0), MidpointRounding.AwayFromZero) * 16639 - control.PositionSamples * 1000000.0 / 44100));
            ConversionDiagnostic warning = Assert.Single(source.Report.Warnings, diagnostic => diagnostic.Code == "NsfTimingQuantized" && diagnostic.SourceEvent == 0);
            Assert.NotNull(warning.MaximumError);
            Assert.InRange(warning.MaximumError.Value, 0, 8319.5);
            Assert.Equal(expectedMaximum, warning.MaximumError.Value, 6);
            Assert.Equal(49L, warning.SourceTick);
            Assert.Equal(0, warning.SourceTrack);
        }

        /// <summary>短音の On／Off と同フレーム内の二回発音を拒否し、部分列を返さない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CollapsedNoteBoundariesAreRejected(bool secondNote)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 1000, lengthTicks: 48);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 1, MidiNote = 69, InstrumentId = 1 });
            if (secondNote)
            {
                song.Tracks[0].Notes.Add(new Note { Tick = 1, DurationTicks = 12, MidiNote = 69, InstrumentId = 1 });
            }
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            Assert.Null(NsfFrameCompiler.Compile(source.Timeline, source.Report));
            Assert.Contains(source.Report.Errors, diagnostic => diagnostic.Code == "NsfEventCollision" && diagnostic.SourceEvent == 0);
            Assert.False(source.Report.CanWrite);
        }

        /// <summary>隣接ノートは全 Off 後に新 On を置き、同値 high の再ロードを保持する。</summary>
        [Fact]
        public void AdjacentNotesKeepOffBeforeOnAndBothTriggers()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 8);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 2, MidiNote = 69, InstrumentId = 1 });
            song.Tracks[0].Notes.Add(new Note { Tick = 2, DurationTicks = 2, MidiNote = 69, InstrumentId = 1 });
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? result = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(result);
            Assert.Equal(new long[] { 0, 1 }, result.Writes.Where(write => write.Address == 0x4003).Select(write => write.Frame));
            Assert.Equal(new[] { (0x4015, 0), (0x4015, 1), (0x4000, 0xBF), (0x4002, 0xFD), (0x4003, 0) },
                result.Writes.Where(write => write.Frame == 1).Select(write => ((int)write.Address, (int)write.Value)));
        }

        /// <summary>On と直後のマクロをレジスタ化前にまとめ、初期 trigger を二重に作らない。</summary>
        [Fact]
        public void OnAndMacroUpdateUseLastControlValueWithOneTrigger()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 8);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 10, 5 } };
            song.Tracks[0].Notes.Add(new Note { Tick = 1, DurationTicks = 5, MidiNote = 69, InstrumentId = 1 });
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? result = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(result);
            Assert.Equal(new byte[] { 0xBA }, result.Writes.Where(write => write.Frame == 1 && write.Address == 0x4000).Select(write => write.Value));
            Assert.Single(result.Writes, write => write.Address == 0x4003);
            ConversionDiagnostic warning = Assert.Single(source.Report.Warnings, diagnostic => diagnostic.Code == "ControlUpdateCoalesced");
            Assert.Equal(1L, warning.SourceTick);
            Assert.Equal(1L, warning.OccurrenceCount);
            Assert.Equal(15, source.Timeline.Events.First(control => control.Kind == ControlEventKind.NoteOn).Volume * 15);
        }

        /// <summary>停止と同じ PLAY に入った旧音のマクロ値を停止前に書かない。</summary>
        [Fact]
        public void OffSupersedesUpdateInSamePlay()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 120, lengthTicks: 8);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 10 } };
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 2, MidiNote = 69, InstrumentId = 1 });
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? result = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(result);
            Assert.DoesNotContain(result.Writes, write => write.Frame == 1 && write.Address == 0x4000);
            Assert.Contains(source.Report.Warnings, diagnostic => diagnostic.Code == "ControlUpdateCoalesced");
        }

        /// <summary>明細保持ゼロの strict でも時刻量子化を隠さず拒否する。</summary>
        [Fact]
        public void StrictUsesTotalWarningsAndCompilationDoesNotMutateSource()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 8);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 4, MidiNote = 69, InstrumentId = 1 });
            string original = SongSerializer.Serialize(song);
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            Assert.Null(NsfFrameCompiler.Compile(source.Timeline, report));
            Assert.True(report.WarningCount > 0);
            Assert.Empty(report.Warnings);
            Assert.Equal(original, SongSerializer.Serialize(song));
        }

        /// <summary>四声の同時発音と有限終端をデータ・ドライバー生成へ接続し、CPU 予算内となる。</summary>
        [Fact]
        public void FourVoiceSongProducesFiniteDataAndAcceptedDriver()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 4);
            song.Instruments.Add(new NesTriangleInstrument { Id = 2 });
            song.Instruments.Add(new NesNoiseInstrument { Id = 3 });
            for (int trackIndex = 0; trackIndex < 4; trackIndex++)
            {
                int instrumentId = trackIndex < 2 ? 1 : trackIndex;
                song.Tracks[trackIndex].Notes.Add(new Note { DurationTicks = 4, MidiNote = 69, InstrumentId = instrumentId });
            }
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? timeline = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(timeline);
            Assert.Equal(2L, timeline.EndFrame);
            Assert.Equal(new[] { (0x4015, 0), (0x4000, 0xB0), (0x4004, 0xB0), (0x400C, 0x30) },
                timeline.Writes.TakeLast(4).Select(write => ((int)write.Address, (int)write.Value)));
            Assert.All(timeline.Writes.TakeLast(4), write => Assert.Equal(2L, write.Frame));
            NsfEncodedData? data = NsfDataEncoder.Encode(timeline, source.Report);
            Assert.NotNull(data);
            Assert.Equal(23, data.MaximumWritesPerPlay);
            Assert.Equal((byte)2, data.Bytes.Last());
            NsfDriverImage? driver = NsfDriverBuilder.Build(data, source.Report);
            Assert.NotNull(driver);
            Assert.Equal(6234L, driver.MaximumPlayCycles);
        }

        /// <summary>Delay 後の開始を量子化し、ミュート時は発音を作らず全停止だけを残す。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DelayAndMuteKeepTheirOriginalControlSemantics(bool muted)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 8);
            song.Tracks[0].Muted = muted;
            song.Tracks[0].Notes.Add(new Note
            {
                DurationTicks = 4, MidiNote = 69, InstrumentId = 1,
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 1) }
            });
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? result = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(result);
            long[] expectedFrames = muted ? Array.Empty<long>() : new long[] { 1 };
            Assert.Equal(expectedFrames, result.Writes.Where(write => write.Address == 0x4003).Select(write => write.Frame));
            Assert.Equal((byte)0, result.Writes.Last(write => write.Address == 0x4015).Value);
        }

        /// <summary>量子化経路でも既存の DPCM 拒否を通し、部分レジスタ列を返さない。</summary>
        [Fact]
        public void DpcmIsStillRejectedAfterQuantization()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 8);
            song.Instruments.Add(new NesDpcmInstrument { Id = 2 });
            song.Tracks[4].Notes.Add(new Note { DurationTicks = 4, InstrumentId = 2 });
            ControlTimelineResult source = CreateControl(song);
            Assert.NotNull(source.Timeline);
            Assert.Null(NsfFrameCompiler.Compile(source.Timeline, source.Report));
            Assert.Contains(source.Report.Errors, diagnostic => diagnostic.Code == "UnsupportedDpcm");
        }

        private static ControlTimelineResult CreateControl(Song song)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Nsf });
    }
}
