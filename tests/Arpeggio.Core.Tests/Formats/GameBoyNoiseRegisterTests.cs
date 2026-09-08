using System;
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
    /// <summary>GB Noise の全周期選択・幅・差分更新と量子化診断を独立した数値で検証する。</summary>
    public sealed class GameBoyNoiseRegisterTests
    {
        private const int NoiseInstrumentId = 3;
        private const int AlternateInstrumentId = 4;
        private const int NoiseEnvelope = 0xFF21;
        private const int NoiseFrequency = 0xFF22;
        private const int NoiseTrigger = 0xFF23;
        private const int Routing = 0xFF25;
        private const int NoiseRouting = 0x88;
        private const int MaximumSelection = 127;
        private const int DivisorCodes = 8;
        private const int MaximumShift = 13;
        private const int ShiftBits = 4;
        private const int WidthBit = 8;

        /// <summary>全 128 selection と両幅で対数距離の最小性・同点の小さい NR43・停止 shift の不使用を検証する。</summary>
        [Theory]
        [InlineData(15, 0)]
        [InlineData(7, WidthBit)]
        public void EverySelectionUsesNearestWorkingRate(int width, int widthFlag)
        {
            for (int selection = 0; selection <= MaximumSelection; selection++)
            {
                Song song = CreateNoiseSong(width, FrameTicks);
                AddNoise(song, selection);
                ControlTimelineResult control = CreateControl(song);
                Assert.NotNull(control.Timeline);
                RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
                Assert.NotNull(timeline);
                int register = Assert.Single(timeline.Writes, write => write.Address == NoiseFrequency).Value;
                Assert.Equal(widthFlag, register & WidthBit);
                Assert.InRange(register >> ShiftBits, 0, MaximumShift);
                AssertNearestRate(selection, register & ~WidthBit);
                Assert.Equal(new[]
                {
                    (NoiseEnvelope, 0), (NoiseFrequency, register), (NoiseEnvelope, 0xF0),
                    (NoiseTrigger, 0x80), (Routing, NoiseRouting)
                }, ValuesAt(timeline, 0).TakeLast(5));
                Assert.Single(timeline.Writes, write => write.Address == NoiseTrigger);
                long targetDivisor = GetTargetDivisor(selection);
                long actualDivisor = GetHardwareDivisor(register & ~WidthBit);
                if (targetDivisor != actualDivisor)
                {
                    Assert.Equal("NoiseRateQuantized", Assert.Single(control.Report.Warnings).Code);
                }
                else
                {
                    Assert.Empty(control.Report.Warnings);
                }
                Assert.DoesNotContain(control.Report.Warnings, warning => warning.Code == "PitchClamped" || warning.Code == "EnvelopeRetriggered");
            }
        }

        /// <summary>低速端・等価分周の同点・最高 selection の NR43 を手計算した固定値へ照合する。</summary>
        [Theory]
        [InlineData(0, 0, 0xD4)]
        [InlineData(1, 0, 0xD7)]
        [InlineData(7, 0, 0xD7)]
        [InlineData(8, 0, 0xC4)]
        [InlineData(10, 0, 0xD6)]
        [InlineData(16, 0, 0xB4)]
        [InlineData(18, 0, 0xC6)]
        [InlineData(69, 0, 0x76)]
        [InlineData(96, 0, 0x14)]
        [InlineData(120, 0, 0x01)]
        [InlineData(127, 0, 0x14)]
        [InlineData(120, 50, 0x01)]
        [InlineData(121, 50, 0x03)]
        [InlineData(0, -10000, 0xD4)]
        [InlineData(127, 10000, 0x14)]
        public void FixedAndModulatedSelectionsMatchKnownRegisters(int midiNote, int pitchCents, int expectedRegister)
        {
            Song song = CreateNoiseSong();
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).PitchMacro = new Macro { Values = new[] { pitchCents } };
            AddNoise(song, midiNote);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(expectedRegister, Assert.Single(timeline.Writes, write => write.Address == NoiseFrequency).Value);
        }

        /// <summary>継続ピッチだけの変更では NR43 の差分だけを書き、同値再発音と幅交換では trigger を保持する。</summary>
        [Fact]
        public void PitchChangesDoNotTriggerAndInstrumentReplacementChangesWidth()
        {
            Song song = CreateNoiseSong(lengthTicks: FrameTicks * 5);
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).PitchMacro = new Macro { Values = new[] { 0, 100, 100 } };
            song.Instruments.Add(new GbNoiseInstrument { Id = AlternateInstrumentId, LfsrWidth = 7 });
            AddNoise(song, 120, durationTicks: FrameTicks * 3);
            AddNote(song, NoiseTrack, FrameTicks * 3, FrameTicks, 120).InstrumentId = AlternateInstrumentId;
            AddNote(song, NoiseTrack, FrameTicks * 4, FrameTicks, 120).InstrumentId = AlternateInstrumentId;
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (NoiseFrequency, 0x02) }, ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[] { 0x01, 0x02, 0x09, 0x09 }, timeline.Writes.Where(write => write.Address == NoiseFrequency).Select(write => (int)write.Value));
            Assert.Equal(new long[] { 0, FrameSamples * 3, FrameSamples * 4 },
                timeline.Writes.Where(write => write.Address == NoiseTrigger).Select(write => write.PositionSamples));
            Assert.All(timeline.Writes.Where(write => write.Address == NoiseTrigger), write => Assert.Equal(0x80, write.Value));
        }

        /// <summary>低速側の丸め損失を元ノートへ集約し、Hz 単位の最大誤差を保持する。</summary>
        [Fact]
        public void QuantizationReportsSourceAndMaximumRateError()
        {
            Song song = CreateNoiseSong();
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).PitchMacro = new Macro { Values = new[] { 0, 600, 600 } };
            AddNote(song, NoiseTrack, FrameTicks, FrameTicks * 3, 1).InstrumentId = NoiseInstrumentId;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            Assert.NotNull(GameBoyRegisterCompiler.Compile(control.Timeline, control.Report));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("NoiseRateQuantized", warning.Code);
            Assert.Equal(NoiseTrack, warning.SourceTrack);
            Assert.Equal(NoiseTrack, warning.OutputTrack);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.Equal(FrameTicks, warning.SourceTick);
            Assert.Null(warning.SourceChannel);
            Assert.Equal(3L, warning.OccurrenceCount);
            Assert.Equal("1", warning.Original);
            Assert.NotNull(warning.Converted);
            Assert.NotNull(warning.MaximumError);
            const double NearestRate = 32.0 / 7;
            Assert.Equal(NearestRate, double.Parse(warning.Converted, CultureInfo.InvariantCulture));
            Assert.Equal(NearestRate - 1, warning.MaximumError.Value, precision: 12);
        }

        /// <summary>保持明細ゼロの strict でも Noise 周期・音量丸めを拒否し、同じ整数音量の再 trigger は増やさない。</summary>
        [Fact]
        public void StrictCountsLossesWithoutRetriggeringUnchangedRoundedVolume()
        {
            Song song = CreateNoiseSong(lengthTicks: FrameTicks * 2);
            AddNoise(song, 1).Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, -1) };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true, diagnosticDetailLimit: 0);
            // 14.5 は 15 を保持するため、再 trigger が不要な丸めも区別する。
            Assert.Null(GameBoyRegisterCompiler.Compile(control.Timeline, report));
            Assert.Empty(report.Warnings);
            Assert.Equal(2L, report.WarningCountsByCode["NoiseRateQuantized"]);
            Assert.Equal(1L, report.WarningCountsByCode["VolumeQuantized"]);
            Assert.DoesNotContain("EnvelopeRetriggered", report.WarningCountsByCode.Keys);
            Assert.Equal(3L, report.DroppedWarningCount);
        }

        /// <summary>ミュートされた Noise は低速・連続パン・マクロから発音や警告を作らない。</summary>
        [Fact]
        public void MutedNoiseDoesNotTriggerOrWarn()
        {
            Song song = CreateNoiseSong(width: 7);
            song.Tracks[NoiseTrack].Muted = true;
            song.Tracks[NoiseTrack].Pan = 0.5;
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).VolumeMacro = new Macro { Values = new[] { 15, 4 } };
            AddNoise(song, 1).Volume = 9;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, report);
            Assert.NotNull(timeline);
            Assert.Empty(report.Warnings);
            Assert.DoesNotContain(timeline.Writes, write => write.Address == NoiseFrequency || write.Address == NoiseTrigger);
            Assert.All(timeline.Writes.Where(write => write.Address == NoiseEnvelope || write.Address == Routing), write => Assert.Equal(0, write.Value));
        }

        /// <summary>Noise のパン境界も左右専用ビットへ写し、連続パンだけを元ノート単位で診断する。</summary>
        [Theory]
        [InlineData(-1, 0x80, false)]
        [InlineData(-0.5001, 0x80, true)]
        [InlineData(-0.5, 0x88, true)]
        [InlineData(0, 0x88, false)]
        [InlineData(0.5, 0x88, true)]
        [InlineData(0.5001, 0x08, true)]
        [InlineData(1, 0x08, false)]
        public void NoisePanUsesItsOwnRoutingBits(double pan, int expectedRouting, bool shouldWarn)
        {
            Song song = CreateNoiseSong();
            song.Tracks[NoiseTrack].Pan = pan;
            AddNoise(song, 120);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(expectedRouting, Assert.Single(timeline.Writes, write => write.Address == Routing && write.Value != 0).Value);
            Assert.Equal(shouldWarn ? 1L : 0L, control.Report.WarningCount);
            if (shouldWarn)
            {
                ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
                Assert.Equal("PanReduced", warning.Code);
                Assert.Equal(NoiseTrack, warning.SourceTrack);
                Assert.Equal(0L, warning.SourceTick);
            }
        }

        private static Song CreateNoiseSong(int width = 15, int lengthTicks = FrameTicks * 4)
        {
            Song song = CreateSong(lengthTicks);
            song.Instruments.Add(new GbNoiseInstrument { Id = NoiseInstrumentId, LfsrWidth = width });
            return song;
        }

        private static Note AddNoise(Song song, int selection, int? durationTicks = null)
        {
            Note note = AddNote(song, NoiseTrack, 0, durationTicks ?? song.LengthTicks, selection);
            note.InstrumentId = NoiseInstrumentId;
            return note;
        }

        private static void AssertNearestRate(int selection, int register)
        {
            long target = GetTargetDivisor(selection);
            long selected = GetHardwareDivisor(register);
            // abs(log(a/b)) の大小は max(a,b)/min(a,b) と同じ。整数の交差積で log 実装を共有しない。
            long selectedNumerator = Math.Max(target, selected);
            long selectedDenominator = Math.Min(target, selected);
            for (int shift = 0; shift <= MaximumShift; shift++)
            {
                for (int divisorCode = 0; divisorCode < DivisorCodes; divisorCode++)
                {
                    int candidateRegister = (shift << ShiftBits) | divisorCode;
                    long candidate = GetHardwareDivisor(candidateRegister);
                    long selectedDistance = selectedNumerator * Math.Min(target, candidate);
                    long candidateDistance = Math.Max(target, candidate) * selectedDenominator;
                    Assert.True(selectedDistance <= candidateDistance, $"selection={selection}, NR43={register:X2}, candidate={candidateRegister:X2}");
                    Assert.True(selectedDistance != candidateDistance || register <= candidateRegister);
                }
            }
        }

        private static long GetTargetDivisor(int selection)
            => (long)(selection % DivisorCodes + 1) << ((MaximumSelection - selection) / DivisorCodes + 1);

        private static long GetHardwareDivisor(int register)
        {
            int divisorCode = register % (1 << ShiftBits);
            return (long)(divisorCode == 0 ? 1 : divisorCode * 2) << (register >> ShiftBits);
        }
    }
}
