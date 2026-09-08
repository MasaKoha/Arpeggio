using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using static Arpeggio.Core.Tests.Formats.GameBoyRegisterTestData;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>GB 変換の音域・量子化診断・strict・状態分離を検証する。</summary>
    public sealed class GameBoyRegisterDiagnosticsTests
    {
        private const int PulseOneEnvelope = 0xFF12;
        private const int PulseOneLow = 0xFF13;
        private const int PulseOneHigh = 0xFF14;
        private const int PulseTwoLow = 0xFF18;
        private const int PulseTwoHigh = 0xFF19;
        private const int WaveLow = 0xFF1D;
        private const int WaveHigh = 0xFF1E;
        private const int Routing = 0xFF25;
        private const int TriggerMask = 0x80;
        private const int FrequencyHighMask = 7;
        private const int FrequencyHighShift = 8;

        /// <summary>三声の連続音域の上下端を register 0 / 2047 へ制限し、元位置と全発生数を保持する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, 0, 0, 0)]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, 69, 100, 2047)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh, 0, 0, 0)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh, 69, 100, 2047)]
        [InlineData(WaveTrack, WaveLow, WaveHigh, 0, 0, 0)]
        [InlineData(WaveTrack, WaveLow, WaveHigh, 69, 100, 2047)]
        public void OutOfRangePitchClampsAndReportsSource(int trackIndex, int lowAddress, int highAddress,
            int midiNote, int pitchOffset, int expectedRegister)
        {
            Song song = CreateSong(FrameTicks * 2);
            var macro = new Macro { Values = new[] { pitchOffset } };
            if (trackIndex == WaveTrack)
            {
                Assert.IsType<GbWaveInstrument>(song.Instruments[1]).ArpeggioMacro = macro;
            }
            else
            {
                Assert.IsType<GbPulseInstrument>(song.Instruments[0]).ArpeggioMacro = macro;
            }
            AddNote(song, trackIndex, 0, song.LengthTicks, midiNote);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            int low = Assert.Single(timeline.Writes, write => write.PositionSamples == 0 && write.Address == lowAddress).Value;
            int high = timeline.Writes.Last(write => write.PositionSamples == 0 && write.Address == highAddress).Value;
            Assert.Equal(expectedRegister, low | ((high & FrequencyHighMask) << FrequencyHighShift));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PitchClamped", warning.Code);
            Assert.Equal(trackIndex, warning.SourceTrack);
            Assert.Equal(trackIndex, warning.OutputTrack);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.Equal(0L, warning.SourceTick);
            Assert.Null(warning.SourceChannel);
            Assert.Equal((midiNote + pitchOffset).ToString(CultureInfo.InvariantCulture), warning.Original);
            Assert.Equal(2L, warning.OccurrenceCount);
            Assert.Equal(2L, control.Report.WarningCountsByCode["PitchClamped"]);
            Assert.True(warning.MaximumError > 0);
        }

        /// <summary>通常の周期量子化は警告にせず、恒常的制限だけなら strict を通す。</summary>
        [Theory]
        [InlineData(PulseOneTrack, 57)]
        [InlineData(PulseTwoTrack, 69)]
        [InlineData(WaveTrack, 60)]
        [InlineData(WaveTrack, 72)]
        public void OrdinaryPitchQuantizationDoesNotWarn(int trackIndex, int midiNote)
        {
            Song song = CreateSong();
            AddNote(song, trackIndex, 0, song.LengthTicks, midiNote);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true);
            Assert.NotNull(GameBoyRegisterCompiler.Compile(control.Timeline, report));
            Assert.Empty(report.Warnings);
            Assert.NotEmpty(report.Limitations);
        }

        /// <summary>変調中のクランプは元ノートへ集約し、誤差最大の入力を代表値として残す。</summary>
        [Fact]
        public void ModulatedPitchWarningsAggregateWithMaximumError()
        {
            Song song = CreateSong();
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).ArpeggioMacro = new Macro { Values = new[] { 0, -100, -200 } };
            AddNote(song, PulseOneTrack, FrameTicks, FrameTicks * 3);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(GameBoyRegisterCompiler.Compile(control.Timeline, control.Report));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PitchClamped", warning.Code);
            Assert.Equal(FrameTicks, warning.SourceTick);
            Assert.Equal(2L, warning.OccurrenceCount);
            Assert.Equal("-131", warning.Original);
            Assert.NotNull(warning.Converted);
            double converted = double.Parse(warning.Converted, CultureInfo.InvariantCulture);
            Assert.Equal(converted + 131, warning.MaximumError);
        }

        /// <summary>Pulse はノート・マクロ適用後の音量へ初期 envelope 音量を掛け、端数を診断する。</summary>
        [Fact]
        public void PulseInitialEnvelopeMultipliesCommonVolume()
        {
            const int InitialEnvelope = 9;
            const int NoteVolume = 9;
            Song song = CreateSong();
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).InitialVolume = InitialEnvelope;
            AddNote(song, PulseOneTrack, 0, song.LengthTicks).Volume = NoteVolume;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(0x50, (int)Assert.Single(timeline.Writes, write => write.Address == PulseOneEnvelope && write.Value > 0).Value);
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("VolumeQuantized", warning.Code);
            Assert.Equal(4L, warning.OccurrenceCount);
            Assert.Equal("5", warning.Converted);
            Assert.NotNull(warning.MaximumError);
            Assert.Equal(0.4, warning.MaximumError.Value, precision: 10);
        }

        /// <summary>Pulse の半整数音量はゼロから遠い整数へ丸め、再 trigger と量子化を別原因として返す。</summary>
        [Fact]
        public void PulseHalfVolumeRoundsAwayFromZero()
        {
            Song song = CreateSong(FrameTicks * 2);
            Note note = AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            note.Volume = 14;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, 1) };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Contains(timeline.Writes, write => write.PositionSamples == FrameSamples &&
                write.Address == PulseOneEnvelope && write.Value == 0xF0);
            Assert.Equal(new[] { "EnvelopeRetriggered", "VolumeQuantized" }, control.Report.Warnings.Select(warning => warning.Code).OrderBy(code => code));
        }

        /// <summary>時間 envelope は各制御フレームで増減し、暫定的な未対応制限を残さない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TimeVaryingEnvelopeUpdatesEveryControlFrame(bool increasing)
        {
            Song song = CreateSong();
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = 9;
            instrument.EnvelopeStepFrames = 1;
            instrument.EnvelopeIncreasing = increasing;
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            int[] expectedVolumes = increasing ? new[] { 0x90, 0xA0, 0xB0, 0xC0 } : new[] { 0x90, 0x80, 0x70, 0x60 };
            Assert.Equal(expectedVolumes, timeline.Writes.Where(write => write.Address == PulseOneEnvelope && write.Value > 0)
                .Select(write => (int)write.Value));
            Assert.Equal(3L, Assert.Single(control.Report.Warnings).OccurrenceCount);
            Assert.DoesNotContain(control.Report.Limitations, limitation => limitation.Contains("未対応"));
        }

        /// <summary>strict は明細保持ゼロでもパン・段階音量・音域の警告全数を確認して部分列を拒否する。</summary>
        [Fact]
        public void StrictRejectsAllLossesEvenWithoutDiagnosticDetails()
        {
            Song song = CreateSong(FrameTicks * 2);
            song.Tracks[WaveTrack].Pan = 0.5;
            AddNote(song, WaveTrack, 0, song.LengthTicks, midiNote: 0).Volume = 1;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true, diagnosticDetailLimit: 0);
            Assert.Null(GameBoyRegisterCompiler.Compile(control.Timeline, report));
            Assert.Equal(5L, report.WarningCount);
            Assert.Equal(report.WarningCount, report.DroppedWarningCount);
            Assert.Empty(report.Warnings);
            Assert.Equal(1L, report.WarningCountsByCode["PanReduced"]);
            Assert.Equal(2L, report.WarningCountsByCode["PitchClamped"]);
            Assert.Equal(2L, report.WarningCountsByCode["WaveVolumeQuantized"]);
            Assert.True(report.Statistics["registerWrites"] > 0);
        }

        /// <summary>ミュートされた低音・端数音量・連続パンからは発音も変換警告も作らない。</summary>
        [Theory]
        [InlineData(PulseOneTrack)]
        [InlineData(PulseTwoTrack)]
        [InlineData(WaveTrack)]
        public void MutedTracksDoNotTriggerOrWarn(int trackIndex)
        {
            Song song = CreateSong();
            song.Tracks[trackIndex].Muted = true;
            song.Tracks[trackIndex].Pan = 0.5;
            AddNote(song, trackIndex, 0, song.LengthTicks, midiNote: 0).Volume = 1;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Empty(control.Report.Warnings);
            Assert.All(timeline.Writes.Where(write => write.Address == Routing), write => Assert.Equal(0, write.Value));
            int[] highAddresses = { PulseOneHigh, PulseTwoHigh, WaveHigh };
            Assert.DoesNotContain(timeline.Writes, write => highAddresses.Contains(write.Address) && (write.Value & TriggerMask) != 0);
        }

        /// <summary>Noise ノートを一定音量で開始し、元ノートの終端で DAC と routing を停止する。</summary>
        [Fact]
        public void NoiseOnsetAndOffAreCompiled()
        {
            const int NoiseInstrument = 3;
            Song song = CreateSong();
            song.Instruments.Add(new GbNoiseInstrument { Id = NoiseInstrument });
            AddNote(song, NoiseTrack, 0, FrameTicks).InstrumentId = NoiseInstrument;
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (0xFF21, 0), (0xFF22, 0x76), (0xFF21, 0xF0), (0xFF23, 0x80), (Routing, 0x88) },
                ValuesAt(timeline, 0).TakeLast(5));
            Assert.Equal(new[] { (0xFF21, 0), (Routing, 0) }, ValuesAt(timeline, FrameSamples));
        }

        /// <summary>元 Song・Wave 配列・レポート・後続変換から確定列を隔離し、同じ制御列の再変換を決定的に保つ。</summary>
        [Fact]
        public void CompiledTimelineIsImmutableAndCompilerStateIsIsolated()
        {
            Song song = CreateSong();
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            AddNote(song, WaveTrack, 0, song.LengthTicks);
            string originalJson = SongSerializer.Serialize(song);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(originalJson, SongSerializer.Serialize(song));
            RegisterWrite[] captured = timeline.Writes.ToArray();
            var writes = Assert.IsAssignableFrom<IList<RegisterWrite>>(timeline.Writes);
            Assert.True(writes.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => writes.Clear());
            Assert.Throws<NotSupportedException>(() => writes[0] = default);
            Assert.IsType<GbWaveInstrument>(song.Instruments[1]).Waveform[0] = FullVolume;
            song.Tracks[PulseOneTrack].Pan = 1;
            song.Tracks[WaveTrack].Notes[0].MidiNote = 0;
            control.Report.AddWarning(new ConversionDiagnostic("LaterWarning", "後続診断による所有権確認。"));
            Assert.NotNull(Compile(song));
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true);
            RegisterTimeline? repeated = GameBoyRegisterCompiler.Compile(control.Timeline, report);
            Assert.NotNull(repeated);
            Assert.Equal(captured, repeated.Writes.ToArray());
            Assert.Equal(captured, timeline.Writes.ToArray());
            Assert.Equal(control.Timeline.EndSamples, repeated.EndSamples);
            Assert.Equal((long)captured.Length, report.Statistics["registerWrites"]);
        }

        /// <summary>先行エラーがある場合は列を公開せず、NES の制御列も GB へ誤変換しない。</summary>
        [Fact]
        public void ExistingErrorAndWrongChipDoNotReturnPartialTimeline()
        {
            ControlTimelineResult control = CreateControl(CreateSong());
            Assert.NotNull(control.Timeline);
            control.Report.AddError(new ConversionDiagnostic("ExistingError", "先行変換の失敗。"));
            Assert.Null(GameBoyRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Single(control.Report.Errors);
            ControlTimelineResult nesControl = CreateControl(SongFactory.Create(ChipKind.Nes));
            Assert.NotNull(nesControl.Timeline);
            Assert.Null(GameBoyRegisterCompiler.Compile(nesControl.Timeline, nesControl.Report));
            Assert.Equal("UnsupportedChip", Assert.Single(nesControl.Report.Errors).Code);
            var wrongReport = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes);
            Assert.Throws<ArgumentException>(() => GameBoyRegisterCompiler.Compile(control.Timeline, wrongReport));
        }
    }
}
