using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>NES の入力ごとの変換警告と、恒常的な制限・strict の区別を検証する。</summary>
    public sealed class NesConversionDiagnosticsTests
    {
        private const int PulseTrack = 0;
        private const int TriangleTrack = 2;
        private const int NoiseTrack = 3;
        private const int TriangleInstrument = 2;
        private const int NoiseInstrument = 3;
        private const int FrameTicks = 2;
        private const int FrameSamples = 735;
        private const int FullVolume = 15;
        private const int ConcertNote = 69;
        private const int PulseHigh = 0x4003;
        private const int TriangleLinear = 0x4008;

        /// <summary>整数でない最終音量は元ノートに集約し、最大誤差と全発生数を保持する。</summary>
        [Theory]
        [InlineData(PulseTrack)]
        [InlineData(NoiseTrack)]
        public void VolumeQuantizationAggregatesPerNoteWithMaximumLevelError(int trackIndex)
        {
            Song song = CreateSong(trackIndex);
            song.Tracks[trackIndex].Notes[0].Volume = 14;
            var volume = new Macro { Values = new[] { 15, 10, 11, 10 } };
            SetVolumeMacro(song, trackIndex, volume);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("VolumeQuantized", warning.Code);
            Assert.Equal(trackIndex, warning.SourceTrack);
            Assert.Equal(trackIndex, warning.OutputTrack);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.Equal(0L, warning.SourceTick);
            Assert.Equal(3L, warning.OccurrenceCount);
            Assert.Equal(3L, control.Report.WarningCountsByCode[warning.Code]);
            Assert.NotNull(warning.MaximumError);
            Assert.Equal(1.0 / 3, warning.MaximumError.Value, precision: 12);
            Assert.Equal("9", warning.Converted);
        }

        /// <summary>0〜15 の整数音量の浮動小数往復誤差は警告にせず、strict でも成功する。</summary>
        [Theory]
        [InlineData(PulseTrack)]
        [InlineData(NoiseTrack)]
        public void IntegerVolumeDoesNotProduceRoundingNoise(int trackIndex)
        {
            for (int volume = 0; volume <= FullVolume; volume++)
            {
                Song song = CreateSong(trackIndex);
                song.Tracks[trackIndex].Notes[0].Volume = volume;
                ControlTimelineResult control = CreateControl(song, strict: true);
                Assert.NotNull(control.Timeline);
                Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
                Assert.Empty(control.Report.Warnings);
            }
        }

        /// <summary>半整数音量はゼロから遠ざけ、音量が整数になる更新では警告を増やさない。</summary>
        [Theory]
        [InlineData(PulseTrack, 0x4000, 0xBF)]
        [InlineData(NoiseTrack, 0x400C, 0x3F)]
        public void HalfVolumeRoundsAwayFromZero(int trackIndex, int controlAddress, int expectedValue)
        {
            Song song = CreateSong(trackIndex);
            song.LengthTicks = FrameTicks * 2;
            Note note = song.Tracks[trackIndex].Notes[0];
            note.DurationTicks = song.LengthTicks;
            note.Volume = 14;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, 1) };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(expectedValue, Assert.Single(timeline.Writes, write => write.PositionSamples == FrameSamples && write.Address == controlAddress).Value);
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("VolumeQuantized", warning.Code);
            Assert.Equal("14.5", warning.Original);
            Assert.Equal("15", warning.Converted);
            Assert.Equal(0.5, warning.MaximumError);
            Assert.Equal(1L, warning.OccurrenceCount);
        }

        /// <summary>Triangle の非 15 音量と VolumeSlide は一件で報告し、固定 linear 値とゲートを維持する。</summary>
        [Theory]
        [InlineData(0, false, 0)]
        [InlineData(14, false, 0)]
        [InlineData(15, true, -15)]
        [InlineData(15, true, 0)]
        [InlineData(14, true, 1)]
        public void TriangleReportsIgnoredVolumeOncePerOnset(int volume, bool hasSlide, int slide)
        {
            Song song = CreateSong(TriangleTrack);
            Note note = song.Tracks[TriangleTrack].Notes[0];
            note.Volume = volume;
            if (hasSlide)
            {
                note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, slide) };
            }
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("TriangleVolumeIgnored", warning.Code);
            Assert.Equal(TriangleTrack, warning.SourceTrack);
            Assert.Equal(1L, warning.OccurrenceCount);
            Assert.Equal("15", warning.Converted);
            Assert.Equal(0xFF, Assert.Single(timeline.Writes, write => write.Address == TriangleLinear).Value);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples > 0 && write.PositionSamples < timeline.EndSamples);
        }

        /// <summary>パンはトラック単位でモノラルへ統合し、元ノートのない位置項目を埋めない。</summary>
        [Theory]
        [InlineData(-1.0)]
        [InlineData(-0.25)]
        [InlineData(0.25)]
        [InlineData(1.0)]
        public void PanWarningHasTrackLocationAndMonoResult(double pan)
        {
            Song song = CreateSong(PulseTrack);
            song.Tracks[PulseTrack].Pan = pan;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PanReduced", warning.Code);
            Assert.Equal(PulseTrack, warning.SourceTrack);
            Assert.Equal(PulseTrack, warning.OutputTrack);
            Assert.Null(warning.SourceTick);
            Assert.Null(warning.SourceEvent);
            Assert.Null(warning.SourceChannel);
            Assert.Equal("0", warning.Converted);
            Assert.Equal(Math.Abs(pan), warning.MaximumError);
            song.Tracks[PulseTrack].Pan = 0;
            ControlTimelineResult centered = CreateControl(song);
            Assert.NotNull(centered.Timeline);
            RegisterTimeline? centeredTimeline = NesRegisterCompiler.Compile(centered.Timeline, centered.Report);
            Assert.NotNull(centeredTimeline);
            Assert.Equal(centeredTimeline.Writes.ToArray(), timeline.Writes.ToArray());
        }

        /// <summary>空の非中央トラックは設定の省略を警告し、ミュート時はその警告も抑止する。</summary>
        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 0)]
        public void EmptyTrackPanHonorsMute(bool muted, int expectedWarnings)
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[NoiseTrack].Pan = 1;
            song.Tracks[NoiseTrack].Muted = muted;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Equal(expectedWarnings, control.Report.WarningCount);
        }

        /// <summary>継続 high の両方向変更だけを位相警告にし、同値更新と On は数えない。</summary>
        [Fact]
        public void PulsePhaseWarningCountsOnlyActualContinuingHighWrites()
        {
            Song song = CreateSong(PulseTrack);
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).ArpeggioMacro = new Macro { Values = new[] { 0, -12, -12, 0 } };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new long[] { 0, FrameSamples, FrameSamples * 3 }, timeline.Writes.Where(write => write.Address == PulseHigh).Select(write => write.PositionSamples));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PulsePhaseRestarted", warning.Code);
            Assert.Equal(2L, warning.OccurrenceCount);
            Assert.Equal("0", warning.Original);
            Assert.Equal("1", warning.Converted);
            Assert.Null(warning.MaximumError);
        }

        /// <summary>6 Hz vibrato が high 境界を往復すると、両 Pulse に実際の二回の位相再開を報告する。</summary>
        [Theory]
        [InlineData(0, 0x4003)]
        [InlineData(1, 0x4007)]
        public void VibratoCrossingTimerHighReportsBothPhaseRestarts(int trackIndex, int highAddress)
        {
            const int VibratoFrames = 11;
            const int VibratoCents = 100;
            Song song = CreateSong(trackIndex);
            song.LengthTicks = FrameTicks * VibratoFrames;
            Note note = song.Tracks[trackIndex].Notes[0];
            note.DurationTicks = song.LengthTicks;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.Vibrato, VibratoCents) };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new long[] { 0, FrameSamples * 6, FrameSamples * 10 },
                timeline.Writes.Where(write => write.Address == highAddress).Select(write => write.PositionSamples));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PulsePhaseRestarted", warning.Code);
            Assert.Equal(trackIndex, warning.SourceTrack);
            Assert.Equal(2L, warning.OccurrenceCount);
        }

        /// <summary>Triangle の継続 high 変更は Pulse の位相再開ではなく、strict の警告対象にしない。</summary>
        [Fact]
        public void TriangleHighChangesDoNotProducePulsePhaseWarning()
        {
            Song song = CreateSong(TriangleTrack);
            Assert.IsType<NesTriangleInstrument>(song.Instruments[TriangleInstrument - 1]).ArpeggioMacro = new Macro
            {
                Values = new[] { 0, -24, -24, 0 }
            };
            ControlTimelineResult control = CreateControl(song, strict: true);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>ミュートは全対応声の非中央パン・音量スライド・範囲外変調の警告を抑止する。</summary>
        [Theory]
        [InlineData(PulseTrack)]
        [InlineData(TriangleTrack)]
        [InlineData(NoiseTrack)]
        public void MutedChannelsDoNotReportDiscardedModulation(int trackIndex)
        {
            Song song = CreateSong(trackIndex);
            song.Tracks[trackIndex].Muted = true;
            song.Tracks[trackIndex].Pan = 1;
            Note note = song.Tracks[trackIndex].Notes[0];
            note.Volume = 14;
            note.Effects = new[]
            {
                new NoteEffect(NoteEffectKind.VolumeSlide, -15),
                new NoteEffect(NoteEffectKind.PitchSlide, -200)
            };
            ControlTimelineResult control = CreateControl(song, strict: true);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>各新規警告は明細保持ゼロでも strict で部分列を拒否する。</summary>
        [Theory]
        [InlineData("VolumeQuantized", PulseTrack)]
        [InlineData("TriangleVolumeIgnored", TriangleTrack)]
        [InlineData("PanReduced", NoiseTrack)]
        [InlineData("PulsePhaseRestarted", PulseTrack)]
        public void StrictRejectsEveryWarningWithoutRetainedDetails(string warningCode, int trackIndex)
        {
            Song song = CreateSong(trackIndex);
            switch (warningCode)
            {
                case "VolumeQuantized":
                    song.Tracks[trackIndex].Notes[0].Volume = 14;
                    SetVolumeMacro(song, trackIndex, new Macro { Values = new[] { 10 } });
                    break;
                case "TriangleVolumeIgnored":
                    song.Tracks[trackIndex].Notes[0].Volume = 0;
                    break;
                case "PanReduced":
                    song.Tracks[trackIndex].Pan = 1;
                    break;
                case "PulsePhaseRestarted":
                    Assert.IsType<NesPulseInstrument>(song.Instruments[0]).ArpeggioMacro = new Macro { Values = new[] { 0, -12 } };
                    break;
            }
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, report));
            Assert.Empty(report.Warnings);
            Assert.Empty(report.Errors);
            Assert.True(report.WarningCountsByCode[warningCode] > 0);
            Assert.Equal(report.WarningCount, report.DroppedWarningCount);
            Assert.False(report.CanWrite);
        }

        /// <summary>方式の制限は通常音の strict を失敗させず、LFSR・位相・DAC 保持を明示する。</summary>
        [Theory]
        [InlineData(PulseTrack)]
        [InlineData(TriangleTrack)]
        [InlineData(NoiseTrack)]
        public void HardwareLimitationsAloneDoNotFailStrict(int trackIndex)
        {
            ControlTimelineResult control = CreateControl(CreateSong(trackIndex), strict: true);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Empty(control.Report.Warnings);
            Assert.Contains(control.Report.Limitations, limitation => limitation.Contains("LFSR seed"));
            Assert.Contains(control.Report.Limitations, limitation => limitation.Contains("Triangle") && limitation.Contains("DAC"));
            Assert.Contains(control.Report.Limitations, limitation => limitation.Contains("無限ループ"));
            Assert.True(control.Report.CanWrite);
        }

        private static Song CreateSong(int trackIndex)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: FrameTicks * 4);
            song.Instruments.Add(new NesTriangleInstrument { Id = TriangleInstrument });
            song.Instruments.Add(new NesNoiseInstrument { Id = NoiseInstrument });
            int instrumentId = trackIndex switch { TriangleTrack => TriangleInstrument, NoiseTrack => NoiseInstrument, _ => 1 };
            song.Tracks[trackIndex].Notes.Add(new Note { DurationTicks = song.LengthTicks, MidiNote = ConcertNote, InstrumentId = instrumentId });
            return song;
        }

        private static void SetVolumeMacro(Song song, int trackIndex, Macro volume)
        {
            if (trackIndex == NoiseTrack)
            {
                Assert.IsType<NesNoiseInstrument>(song.Instruments[NoiseInstrument - 1]).VolumeMacro = volume;
                return;
            }
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).VolumeMacro = volume;
        }

        private static ControlTimelineResult CreateControl(Song song, bool strict = false)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Strict = strict });
    }
}
