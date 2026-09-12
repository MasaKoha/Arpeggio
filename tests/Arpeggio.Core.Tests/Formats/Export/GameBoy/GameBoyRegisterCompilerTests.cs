using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using static Arpeggio.Core.Tests.Formats.Export.GameBoy.GameBoyRegisterTestData;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.GameBoy;

namespace Arpeggio.Core.Tests.Formats.Export.GameBoy
{
    /// <summary>GB の周期・波形 packing・段階音量・パンを独立した固定値で検証する。</summary>
    public sealed class GameBoyRegisterCompilerTests
    {
        private const int Power = 0xFF26;
        private const int MasterVolume = 0xFF24;
        private const int Routing = 0xFF25;
        private const int Sweep = 0xFF10;
        private const int PulseOneDuty = 0xFF11;
        private const int PulseOneEnvelope = 0xFF12;
        private const int PulseOneLow = 0xFF13;
        private const int PulseOneHigh = 0xFF14;
        private const int PulseTwoDuty = 0xFF16;
        private const int PulseTwoEnvelope = 0xFF17;
        private const int PulseTwoLow = 0xFF18;
        private const int PulseTwoHigh = 0xFF19;
        private const int WaveDac = 0xFF1A;
        private const int WaveVolume = 0xFF1C;
        private const int WaveLow = 0xFF1D;
        private const int WaveHigh = 0xFF1E;
        private const int WaveRamStart = 0xFF30;
        private const int WaveRamEnd = 0xFF3F;
        private const int InitializationWrites = 5;

        /// <summary>先頭無音でも時刻 0 に規定順で初期化し、演奏中に電源を再リセットしない。</summary>
        [Fact]
        public void InitializationPrecedesDelayedFirstNote()
        {
            Song song = CreateSong();
            AddNote(song, PulseOneTrack, FrameTicks, FrameTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[]
            {
                (Power, 0), (Power, 0x80), (MasterVolume, 0x77), (Routing, 0), (Sweep, 0)
            }, ValuesAt(timeline, 0));
            Assert.Equal(2, timeline.Writes.Count(write => write.Address == Power));
            Assert.Single(timeline.Writes, write => write.Address == MasterVolume);
            Assert.Equal(Enumerable.Range(0, timeline.Writes.Count), timeline.Writes.Select(write => write.Order));
            Assert.Equal(timeline.Writes.Select(write => write.PositionSamples).OrderBy(position => position),
                timeline.Writes.Select(write => write.PositionSamples));
            Assert.Equal(ChipKind.GameBoy, timeline.Chip);
        }

        /// <summary>A4 の Pulse register=1750 と DAC off → 周期設定 → trigger → routing の順を固定する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneDuty, PulseOneEnvelope, PulseOneLow, PulseOneHigh, 0x11)]
        [InlineData(PulseTwoTrack, PulseTwoDuty, PulseTwoEnvelope, PulseTwoLow, PulseTwoHigh, 0x22)]
        public void PulseConcertPitchAndTriggerOrderMatchFixedValues(int trackIndex, int dutyAddress,
            int envelopeAddress, int lowAddress, int highAddress, int routing)
        {
            Song song = CreateSong();
            AddNote(song, trackIndex, 0, FrameTicks);
            Assert.Equal(new[]
            {
                (envelopeAddress, 0), (dutyAddress, 0x80), (lowAddress, 0xD6), (highAddress, 0x06),
                (envelopeAddress, 0xF0), (highAddress, 0x86), (Routing, routing)
            }, ValuesAt(Compile(song), 0).Skip(InitializationWrites));
        }

        /// <summary>両 Pulse の四つのデューティを NR11 / NR21 の上位 2 bit に写す。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneDuty, DutyCycle.Percent12_5, 0x00)]
        [InlineData(PulseOneTrack, PulseOneDuty, DutyCycle.Percent25, 0x40)]
        [InlineData(PulseOneTrack, PulseOneDuty, DutyCycle.Percent50, 0x80)]
        [InlineData(PulseOneTrack, PulseOneDuty, DutyCycle.Percent75, 0xC0)]
        [InlineData(PulseTwoTrack, PulseTwoDuty, DutyCycle.Percent12_5, 0x00)]
        [InlineData(PulseTwoTrack, PulseTwoDuty, DutyCycle.Percent25, 0x40)]
        [InlineData(PulseTwoTrack, PulseTwoDuty, DutyCycle.Percent50, 0x80)]
        [InlineData(PulseTwoTrack, PulseTwoDuty, DutyCycle.Percent75, 0xC0)]
        public void PulseDutyUsesOnlyDutyBits(int trackIndex, int address, DutyCycle duty, int expected)
        {
            Song song = CreateSong();
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).Duty = duty;
            AddNote(song, trackIndex, 0, FrameTicks);
            Assert.Equal(expected, (int)Assert.Single(Compile(song).Writes, write => write.Address == address).Value);
        }

        /// <summary>Wave の全 16 byte を上位ニブル先行で詰め、A4 register=1899 を設定してから trigger する。</summary>
        [Fact]
        public void WaveRamPackingAndConcertPitchHaveExactOnsetSequence()
        {
            Song song = CreateSong();
            AddNote(song, WaveTrack, 0, FrameTicks);
            var expectedRam = new[]
            {
                (0xFF30, 0x01), (0xFF31, 0x23), (0xFF32, 0x45), (0xFF33, 0x67),
                (0xFF34, 0x89), (0xFF35, 0xAB), (0xFF36, 0xCD), (0xFF37, 0xEF),
                (0xFF38, 0xFE), (0xFF39, 0xDC), (0xFF3A, 0xBA), (0xFF3B, 0x98),
                (0xFF3C, 0x76), (0xFF3D, 0x54), (0xFF3E, 0x32), (0xFF3F, 0x10)
            };
            var expected = new[] { (WaveDac, 0) }.Concat(expectedRam).Concat(new[]
            {
                (WaveVolume, 0x20), (WaveLow, 0x6B), (WaveDac, 0x80), (WaveHigh, 0x87), (Routing, 0x44)
            });
            Assert.Equal(expected, ValuesAt(Compile(song), 0).Skip(InitializationWrites));
        }

        /// <summary>正確な 0 / 25 / 50 / 100% は NR32 の規定値になり、近似警告を出さない。</summary>
        [Theory]
        [InlineData(0, 0x00)]
        [InlineData(25, 0x60)]
        [InlineData(50, 0x40)]
        [InlineData(100, 0x20)]
        public void ExactWaveLevelsUseHardwareVolumeCodes(int outputLevel, int expected)
        {
            Song song = CreateSong();
            Assert.IsType<GbWaveInstrument>(song.Instruments[1]).OutputLevel = outputLevel;
            AddNote(song, WaveTrack, 0, song.LengthTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(expected, (int)Assert.Single(timeline.Writes, write => write.Address == WaveVolume).Value);
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>段階間の中点は必ず小さい音量へ寄せ、継続中に RAM と trigger を繰り返さない。</summary>
        [Theory]
        [InlineData(25, 7, 4, 0.125, 0x00)]
        [InlineData(50, 11, 8, 0.375, 0x60)]
        [InlineData(100, 11, 8, 0.75, 0x40)]
        public void WaveVolumeTiesChooseLowerLevel(int outputLevel, int noteVolume, int lengthTicks,
            double expectedTarget, int expectedRegister)
        {
            Song song = CreateSong(lengthTicks);
            Assert.IsType<GbWaveInstrument>(song.Instruments[1]).OutputLevel = outputLevel;
            Note note = AddNote(song, WaveTrack, 0, lengthTicks);
            note.Volume = noteVolume;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, 1) };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            ControlEvent update = Assert.Single(control.Timeline.Events,
                state => state.Kind == ControlEventKind.Update && state.PositionSamples == FrameSamples);
            const double PercentScale = 100;
            Assert.Equal(expectedTarget, update.Volume * outputLevel / PercentScale);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(expectedRegister, (int)timeline.Writes.Last(write => write.Address == WaveVolume && write.PositionSamples <= FrameSamples).Value);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples > 0 &&
                (write.Address == WaveHigh || (write.Address >= WaveRamStart && write.Address <= WaveRamEnd)));
            Assert.Contains(control.Report.Warnings, warning => warning.Code == "WaveVolumeQuantized");
        }

        /// <summary>連続的な音量変化は NR32 だけで四段階を通り、DAC・RAM・周期・trigger を更新しない。</summary>
        [Fact]
        public void WaveVolumeSlideUpdatesOnlyVolumeRegister()
        {
            const int LengthTicks = FrameTicks * 5;
            Song song = CreateSong(LengthTicks);
            Note note = AddNote(song, WaveTrack, 0, LengthTicks);
            note.Volume = 0;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, FullVolume) };
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (WaveVolume, 0x60) }, ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (WaveVolume, 0x40) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 3));
            Assert.Equal(new[] { (WaveVolume, 0x20) }, ValuesAt(timeline, FrameSamples * 4));
        }

        /// <summary>境界 ±0.5 は両側、境界外は片側へ量子化し、三声の対応ビットを使う。</summary>
        [Theory]
        [InlineData(PulseOneTrack, -1, 0x10, false)]
        [InlineData(PulseOneTrack, -0.5001, 0x10, true)]
        [InlineData(PulseOneTrack, -0.5, 0x11, true)]
        [InlineData(PulseOneTrack, 0, 0x11, false)]
        [InlineData(PulseOneTrack, 0.5, 0x11, true)]
        [InlineData(PulseOneTrack, 0.5001, 0x01, true)]
        [InlineData(PulseOneTrack, 1, 0x01, false)]
        [InlineData(PulseTwoTrack, -1, 0x20, false)]
        [InlineData(PulseTwoTrack, -0.5001, 0x20, true)]
        [InlineData(PulseTwoTrack, -0.5, 0x22, true)]
        [InlineData(PulseTwoTrack, 0, 0x22, false)]
        [InlineData(PulseTwoTrack, 0.5, 0x22, true)]
        [InlineData(PulseTwoTrack, 0.5001, 0x02, true)]
        [InlineData(PulseTwoTrack, 1, 0x02, false)]
        [InlineData(WaveTrack, -1, 0x40, false)]
        [InlineData(WaveTrack, -0.5001, 0x40, true)]
        [InlineData(WaveTrack, -0.5, 0x44, true)]
        [InlineData(WaveTrack, 0, 0x44, false)]
        [InlineData(WaveTrack, 0.5, 0x44, true)]
        [InlineData(WaveTrack, 0.5001, 0x04, true)]
        [InlineData(WaveTrack, 1, 0x04, false)]
        public void PanQuantizationUsesChannelRoutingBits(int trackIndex, double pan, int expectedRouting, bool shouldWarn)
        {
            Song song = CreateSong();
            song.Tracks[trackIndex].Pan = pan;
            AddNote(song, trackIndex, FrameTicks, FrameTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(expectedRouting, (int)Assert.Single(timeline.Writes,
                write => write.Address == Routing && write.Value != 0).Value);
            Assert.Equal(shouldWarn ? 1L : 0L, control.Report.WarningCount);
            if (shouldWarn)
            {
                ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
                Assert.Equal("PanReduced", warning.Code);
                Assert.Equal(trackIndex, warning.SourceTrack);
                Assert.Equal(FrameTicks, warning.SourceTick);
                Assert.Equal(0L, warning.SourceEvent);
                Assert.Equal(trackIndex, warning.OutputTrack);
            }
        }
    }
}
