using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>PCM を生成せず、NES の固定レジスタ値と書き込み副作用の順序を検証する。</summary>
    public sealed class NesRegisterCompilerTests
    {
        private const int PulseOneTrack = 0;
        private const int PulseTwoTrack = 1;
        private const int TriangleTrack = 2;
        private const int TriangleInstrument = 2;
        private const int ConcertNote = 69;
        private const int FullVolume = 15;
        private const int FrameSamples = 735;
        private const int FrameTicks = 2;
        private const int InitializationWrites = 6;
        private const int TimerHighShift = 8;
        private const int TimerHighMask = 7;
        private const int MaximumTimer = 2047;
        private const int MinimumTimer = 8;
        private const int SweepEnable = 0x80;
        private const int SweepNegate = 0x08;
        private const int SweepShiftMask = 0x07;
        private const int LengthIndexMask = 0xF8;
        private const int PulseOneControl = 0x4000;
        private const int PulseOneSweep = 0x4001;
        private const int PulseOneLow = 0x4002;
        private const int PulseOneHigh = 0x4003;
        private const int PulseTwoControl = 0x4004;
        private const int PulseTwoSweep = 0x4005;
        private const int PulseTwoLow = 0x4006;
        private const int PulseTwoHigh = 0x4007;
        private const int TriangleLinear = 0x4008;
        private const int TriangleLow = 0x400A;
        private const int TriangleHigh = 0x400B;
        private const int DmcControl = 0x4010;
        private const int DmcOutput = 0x4011;
        private const int Status = 0x4015;
        private const int FrameCounter = 0x4017;

        /// <summary>先頭無音でも時刻 0 に全初期化を規定順で書く。</summary>
        [Fact]
        public void InitializationPrecedesDelayedFirstNote()
        {
            Song song = CreateSong();
            song.Tracks[PulseOneTrack].Notes.Add(new Note { Tick = FrameTicks, DurationTicks = FrameTicks, MidiNote = ConcertNote });
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[]
            {
                (Status, 0), (DmcControl, 0), (DmcOutput, 0),
                (PulseOneSweep, 0x08), (PulseTwoSweep, 0x08), (FrameCounter, 0xC0)
            }, ValuesAt(timeline, 0));
            Assert.Equal(Enumerable.Range(0, timeline.Writes.Count), timeline.Writes.Select(write => write.Order));
            Assert.True(timeline.Writes.Select(write => write.PositionSamples).SequenceEqual(
                timeline.Writes.Select(write => write.PositionSamples).OrderBy(position => position)));
            Assert.Equal(ChipKind.Nes, timeline.Chip);
        }

        /// <summary>A4 の timer と 50% / 音量 15 の packing を独立した固定値で確かめる。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneControl, PulseOneLow, PulseOneHigh, 253, 0xBF)]
        [InlineData(PulseTwoTrack, PulseTwoControl, PulseTwoLow, PulseTwoHigh, 253, 0xBF)]
        [InlineData(TriangleTrack, TriangleLinear, TriangleLow, TriangleHigh, 126, 0xFF)]
        public void ConcertPitchHasExpectedTimerAndControl(int trackIndex, int controlAddress, int lowAddress,
            int highAddress, int expectedTimer, int expectedControl)
        {
            Song song = CreateSong();
            AddNote(song, trackIndex, 0, FrameTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(expectedTimer, TimerAt(timeline, 0, lowAddress, highAddress));
            Assert.Equal(expectedControl, (int)Assert.Single(timeline.Writes, write => write.Address == controlAddress).Value);
            Assert.Equal(0, Assert.Single(timeline.Writes, write => write.Address == highAddress).Value & LengthIndexMask);
        }

        /// <summary>低音の sweep は無効かつ減算方向で、加算 target overflow による消音条件を避ける。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneSweep, PulseOneLow, PulseOneHigh)]
        [InlineData(PulseTwoTrack, PulseTwoSweep, PulseTwoLow, PulseTwoHigh)]
        public void LowPulseDisablesSweepWithoutOverflowMute(int trackIndex, int sweepAddress, int lowAddress, int highAddress)
        {
            const int LowNote = 33;
            const int ExpectedTimer = 2033;
            Song song = CreateSong();
            AddNote(song, trackIndex, 0, FrameTicks, LowNote);
            RegisterTimeline timeline = Compile(song);
            int sweep = Assert.Single(timeline.Writes, write => write.Address == sweepAddress).Value;
            int timer = TimerAt(timeline, 0, lowAddress, highAddress);
            Assert.Equal(ExpectedTimer, timer);
            Assert.Equal(SweepNegate, sweep);
            Assert.Equal(0, sweep & SweepEnable);
            int shiftedTimer = timer >> (sweep & SweepShiftMask);
            // enable=0 だけでは加算 target の overflow 判定を解除できない。
            Assert.True(timer + shiftedTimer > MaximumTimer);
            int negateCorrection = trackIndex == PulseOneTrack ? 1 : 0;
            int target = timer - shiftedTimer - negateCorrection;
            Assert.False(timer < MinimumTimer || target > MaximumTimer);
        }

        /// <summary>上位 3 bit が同じ vibrato は low だけ更新し、同音の次 On は high を再ロードする。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh)]
        [InlineData(TriangleTrack, TriangleLow, TriangleHigh)]
        public void VibratoKeepsHighUntilSamePitchIsRetriggered(int trackIndex, int lowAddress, int highAddress)
        {
            const int VibratoCents = 10;
            const int VibratoTicks = 20;
            Song song = CreateSong(VibratoTicks + FrameTicks);
            AddNote(song, trackIndex, 0, VibratoTicks);
            song.Tracks[trackIndex].Notes[0].Effects = new[] { new NoteEffect(NoteEffectKind.Vibrato, VibratoCents) };
            AddNote(song, trackIndex, VibratoTicks, FrameTicks);
            RegisterTimeline timeline = Compile(song);
            long retriggerSamples = VibratoTicks / FrameTicks * FrameSamples;
            Assert.Equal(new long[] { 0, retriggerSamples }, timeline.Writes.Where(write => write.Address == highAddress).Select(write => write.PositionSamples));
            Assert.Contains(timeline.Writes, write => write.Address == lowAddress && write.PositionSamples > 0 && write.PositionSamples < retriggerSamples);
            Assert.Contains(timeline.Writes, write => write.Address == lowAddress && write.PositionSamples == retriggerSamples);
        }

        /// <summary>継続音の high 境界を両方向に越える時だけ low → high の順で更新する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, -12, 253)]
        [InlineData(TriangleTrack, TriangleLow, TriangleHigh, -24, 126)]
        public void ContinuingPitchWritesHighOnlyWhenItsBitsChange(int trackIndex, int lowAddress,
            int highAddress, int pitchOffset, int originalLow)
        {
            const int CrossingLow = 0xFB;
            Song song = CreateSong();
            var arpeggio = new Macro { Values = new[] { 0, pitchOffset, pitchOffset, 0 } };
            if (trackIndex == TriangleTrack)
            {
                Assert.IsType<NesTriangleInstrument>(song.Instruments[1]).ArpeggioMacro = arpeggio;
            }
            else
            {
                Assert.IsType<NesPulseInstrument>(song.Instruments[0]).ArpeggioMacro = arpeggio;
            }
            AddNote(song, trackIndex, 0, song.LengthTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (lowAddress, CrossingLow), (highAddress, 1) }, ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[] { (lowAddress, originalLow), (highAddress, 0) }, ValuesAt(timeline, FrameSamples * 3));
        }

        /// <summary>timer 319 → 63 は low が同じなので high だけ変更し、継続 Triangle を再起動しない。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, 65)]
        [InlineData(TriangleTrack, TriangleLow, TriangleHigh, 53)]
        public void HighOnlyChangeDoesNotRewriteLowOrTriggerTriangle(int trackIndex, int lowAddress, int highAddress, int midiNote)
        {
            const int PitchOffset = 28;
            const int InitialTimer = 319;
            Song song = CreateSong(FrameTicks * 2);
            var arpeggio = new Macro { Values = new[] { 0, PitchOffset } };
            if (trackIndex == TriangleTrack)
            {
                Assert.IsType<NesTriangleInstrument>(song.Instruments[1]).ArpeggioMacro = arpeggio;
            }
            else
            {
                Assert.IsType<NesPulseInstrument>(song.Instruments[0]).ArpeggioMacro = arpeggio;
            }
            AddNote(song, trackIndex, 0, song.LengthTicks, midiNote);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(InitialTimer, TimerAt(timeline, 0, lowAddress, highAddress));
            Assert.Equal(new[] { (highAddress, 0) }, ValuesAt(timeline, FrameSamples));
        }

        /// <summary>デューティマクロと半整数の音量を反映し、継続の同値 timer は書かない。</summary>
        [Fact]
        public void DutyAndVolumeUpdateWithoutReloadingTimer()
        {
            const int InitialVolume = 14;
            const int InitialControl = 0x3E;
            const int RoundedControl = 0xFF;
            Song song = CreateSong(FrameTicks * 2);
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).DutyMacro = new Macro { Values = new[] { 1, 4 } };
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            Note note = song.Tracks[PulseOneTrack].Notes[0];
            note.Volume = InitialVolume;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, 1) };
            RegisterTimeline timeline = Compile(song);
            Assert.Contains(timeline.Writes, write => write.PositionSamples == 0 && write.Address == PulseOneControl && write.Value == InitialControl);
            Assert.Equal(new[] { (PulseOneControl, RoundedControl) }, ValuesAt(timeline, FrameSamples));
        }

        /// <summary>Triangle は enable と linear / length を設定した後に即時クロックを書き、Off で二つの Pulse を維持する。</summary>
        [Fact]
        public void TriangleStartsInOrderAndStopsWithoutDisablingPulses()
        {
            Song song = CreateSong();
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            AddNote(song, PulseTwoTrack, 0, song.LengthTicks);
            AddNote(song, TriangleTrack, FrameTicks, FrameTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[]
            {
                (Status, 7), (TriangleLinear, 0xFF), (TriangleLow, 126),
                (TriangleHigh, 0), (FrameCounter, 0xC0)
            }, ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (Status, 3) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples > FrameSamples && write.Address == TriangleLinear);
        }

        /// <summary>全 Off のトラック順が全 On に先行し、length の前に各 enable を復元する。</summary>
        [Fact]
        public void SimultaneousReplacementStopsAllTracksBeforeStartingAnyTrack()
        {
            Song song = CreateSong();
            for (int trackIndex = PulseOneTrack; trackIndex <= TriangleTrack; trackIndex++)
            {
                AddNote(song, trackIndex, 0, FrameTicks);
                AddNote(song, trackIndex, FrameTicks, FrameTicks);
            }
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[]
            {
                (Status, 6), (Status, 4), (Status, 0),
                (Status, 1), (PulseOneControl, 0xBF), (PulseOneLow, 253), (PulseOneHigh, 0),
                (Status, 3), (PulseTwoControl, 0xBF), (PulseTwoLow, 253), (PulseTwoHigh, 0),
                (Status, 7), (TriangleLinear, 0xFF), (TriangleLow, 126), (TriangleHigh, 0), (FrameCounter, 0xC0)
            }, ValuesAt(timeline, FrameSamples));
        }

        /// <summary>二周目は loopStartTick から再発音し、初期化は繰り返さず同値 On の全書き込みを保つ。</summary>
        [Fact]
        public void FiniteLoopRepeatsRegisterSequenceFromLoopStart()
        {
            const int SongTicks = 6;
            const int LoopStartTicks = 2;
            const int Loops = 2;
            Song song = CreateSong(SongTicks);
            song.LoopStartTick = LoopStartTicks;
            AddNote(song, PulseOneTrack, LoopStartTicks, FrameTicks);
            AddNote(song, TriangleTrack, LoopStartTicks, FrameTicks);
            RegisterTimeline timeline = Compile(song, Loops);
            Assert.Equal(5L * FrameSamples, timeline.EndSamples);
            Assert.Equal(InitializationWrites, timeline.Writes.Count(write => write.PositionSamples == 0));
            Assert.Equal(ValuesAt(timeline, FrameSamples), ValuesAt(timeline, FrameSamples * 3));
            Assert.Equal(ValuesAt(timeline, FrameSamples * 2), ValuesAt(timeline, FrameSamples * 4));
            Assert.Equal(new[] { (Status, 4), (Status, 0) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new long[] { FrameSamples, FrameSamples * 3 }, timeline.Writes.Where(write => write.Address == PulseOneHigh).Select(write => write.PositionSamples));
        }

        private static Song CreateSong(int lengthTicks = FrameTicks * 4)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: lengthTicks);
            song.Instruments.Add(new NesTriangleInstrument { Id = TriangleInstrument });
            return song;
        }

        private static void AddNote(Song song, int trackIndex, int tick, int durationTicks, int midiNote = ConcertNote)
        {
            song.Tracks[trackIndex].Notes.Add(new Note
            {
                Tick = tick, DurationTicks = durationTicks, MidiNote = midiNote, Volume = FullVolume,
                InstrumentId = trackIndex == TriangleTrack ? TriangleInstrument : 1
            });
        }

        private static RegisterTimeline Compile(Song song, int loops = 1)
        {
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.True(control.Report.CanWrite);
            Assert.NotNull(timeline);
            return timeline;
        }

        private static (int Address, int Value)[] ValuesAt(RegisterTimeline timeline, long positionSamples)
            => timeline.Writes.Where(write => write.PositionSamples == positionSamples)
                .Select(write => ((int)write.Address, (int)write.Value)).ToArray();

        private static int TimerAt(RegisterTimeline timeline, long positionSamples, int lowAddress, int highAddress)
        {
            int low = Assert.Single(timeline.Writes, write => write.PositionSamples == positionSamples && write.Address == lowAddress).Value;
            int high = Assert.Single(timeline.Writes, write => write.PositionSamples == positionSamples && write.Address == highAddress).Value;
            return low | ((high & TimerHighMask) << TimerHighShift);
        }
    }
}
