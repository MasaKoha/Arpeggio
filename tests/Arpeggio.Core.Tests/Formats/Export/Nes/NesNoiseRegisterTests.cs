using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nes;

namespace Arpeggio.Core.Tests.Formats.Export.Nes
{
    /// <summary>NES Noise の周期選択・mode・音量・ゲートをレジスタ列で検証する。</summary>
    public sealed class NesNoiseRegisterTests
    {
        private const int NoiseTrack = 3;
        private const int NoiseInstrument = 2;
        private const int AlternateInstrument = 3;
        private const int FrameTicks = 2;
        private const int FrameSamples = 735;
        private const int PeriodCount = 16;
        private const int NoiseControl = 0x400C;
        private const int NoisePeriod = 0x400E;
        private const int NoiseLength = 0x400F;
        private const int Status = 0x4015;
        private const int NoiseEnable = 0x08;
        private const int FullControl = 0x3F;
        private const int SilentControl = 0x30;

        /// <summary>両 mode の全 16 周期を、enable → 音量 → 周期 → length の固定順で出力する。</summary>
        [Theory]
        [InlineData(NoiseMode.Long, 0x00)]
        [InlineData(NoiseMode.Short, 0x80)]
        public void EveryPeriodHasExpectedModeAndOnsetOrder(NoiseMode mode, int modeBits)
        {
            for (int periodIndex = 0; periodIndex < PeriodCount; periodIndex++)
            {
                Song song = CreateSong(mode, FrameTicks);
                song.Tracks[NoiseTrack].Notes.Add(new Note
                {
                    DurationTicks = FrameTicks, MidiNote = periodIndex, InstrumentId = NoiseInstrument
                });
                RegisterTimeline timeline = Compile(song);
                Assert.Equal(new[]
                {
                    (Status, NoiseEnable), (NoiseControl, FullControl),
                    (NoisePeriod, modeBits + periodIndex), (NoiseLength, 0)
                }, ValuesAt(timeline, 0).TakeLast(4));
                Assert.Equal(new long[] { 0 }, timeline.Writes.Where(write => write.Address == NoiseLength).Select(write => write.PositionSamples));
            }
        }

        /// <summary>変調後 selection をクランプして ToEven で丸め、下位 4 bit を使う。音階クランプ警告は出さない。</summary>
        [Theory]
        [InlineData(0, -50, 0)]
        [InlineData(0, -10000, 0)]
        [InlineData(0, 50, 0)]
        [InlineData(1, 50, 2)]
        [InlineData(15, 50, 0)]
        [InlineData(16, 0, 0)]
        [InlineData(68, 50, 4)]
        [InlineData(69, 50, 6)]
        [InlineData(127, 0, 15)]
        [InlineData(127, 10000, 15)]
        public void ModulatedSelectionClampsRoundsToEvenAndWraps(int midiNote, int pitchCents, int expectedPeriod)
        {
            Song song = CreateSong(NoiseMode.Long, FrameTicks);
            Assert.IsType<NesNoiseInstrument>(song.Instruments[1]).PitchMacro = new Macro { Values = new[] { pitchCents } };
            song.Tracks[NoiseTrack].Notes.Add(new Note
            {
                DurationTicks = FrameTicks, MidiNote = midiNote, InstrumentId = NoiseInstrument
            });
            ControlTimelineResult control = CreateControl(song, strict: true);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(expectedPeriod, Assert.Single(timeline.Writes, write => write.Address == NoisePeriod).Value);
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>周期と音量の継続変更は差分だけを書き、同値 On の length は省略しない。</summary>
        [Fact]
        public void ContinuingChangesAndRepeatedOnsetsPreserveLengthSideEffects()
        {
            const int FirstDuration = FrameTicks * 4;
            Song song = CreateSong(NoiseMode.Short, FirstDuration + FrameTicks);
            var instrument = Assert.IsType<NesNoiseInstrument>(song.Instruments[1]);
            instrument.PitchMacro = new Macro { Values = new[] { 0, 100, 100, 0 }, LoopIndex = 0 };
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 0, 15, 15 } };
            song.Tracks[NoiseTrack].Notes.Add(new Note { DurationTicks = FirstDuration, MidiNote = 0, InstrumentId = NoiseInstrument });
            song.Tracks[NoiseTrack].Notes.Add(new Note { Tick = FirstDuration, DurationTicks = FrameTicks, MidiNote = 0, InstrumentId = NoiseInstrument });
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (NoiseControl, SilentControl), (NoisePeriod, 0x81) }, ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (NoiseControl, FullControl) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[] { (NoisePeriod, 0x80) }, ValuesAt(timeline, FrameSamples * 3));
            Assert.Equal(new[]
            {
                (Status, 0), (Status, NoiseEnable), (NoiseControl, FullControl), (NoisePeriod, 0x80), (NoiseLength, 0)
            }, ValuesAt(timeline, FrameSamples * 4));
            Assert.Equal(new long[] { 0, FrameSamples * 4 }, timeline.Writes.Where(write => write.Address == NoiseLength).Select(write => write.PositionSamples));
        }

        /// <summary>隣接ノートで音色を交換すると mode が切り替わり、継続同値周期の書き込みは増えない。</summary>
        [Fact]
        public void InstrumentReplacementChangesModeAndHeldMacrosDoNotRewriteRegisters()
        {
            Song song = CreateSong(NoiseMode.Long, FrameTicks * 4);
            song.Instruments.Add(new NesNoiseInstrument { Id = AlternateInstrument, NoiseMode = NoiseMode.Short });
            song.Tracks[NoiseTrack].Notes.Add(new Note { DurationTicks = FrameTicks * 2, MidiNote = 0, InstrumentId = NoiseInstrument });
            song.Tracks[NoiseTrack].Notes.Add(new Note { Tick = FrameTicks * 2, DurationTicks = FrameTicks * 2, MidiNote = 0, InstrumentId = AlternateInstrument });
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new byte[] { 0, 0x80 }, timeline.Writes.Where(write => write.Address == NoisePeriod).Select(write => write.Value));
            Assert.Empty(ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 3));
        }

        /// <summary>ミュートは Noise の発音・変調・パン警告を抑止し、終端の消音値だけを残す。</summary>
        [Fact]
        public void MutedNoiseDoesNotEnableOrWarn()
        {
            Song song = CreateSong(NoiseMode.Short, FrameTicks * 3);
            song.Tracks[NoiseTrack].Muted = true;
            song.Tracks[NoiseTrack].Pan = 1;
            song.Tracks[NoiseTrack].Notes.Add(new Note { DurationTicks = song.LengthTicks, Volume = 14, InstrumentId = NoiseInstrument });
            Assert.IsType<NesNoiseInstrument>(song.Instruments[1]).VolumeMacro = new Macro { Values = new[] { 10 } };
            ControlTimelineResult control = CreateControl(song, strict: true);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Empty(control.Report.Warnings);
            Assert.DoesNotContain(timeline.Writes, write => write.Address == NoisePeriod || write.Address == NoiseLength);
            Assert.All(timeline.Writes.Where(write => write.Address == Status), write => Assert.Equal(0, write.Value));
            RegisterWrite silence = Assert.Single(timeline.Writes, write => write.Address == NoiseControl);
            Assert.Equal(timeline.EndSamples, silence.PositionSamples);
            Assert.Equal(SilentControl, silence.Value);
        }

        /// <summary>Delay 後の開始と元終端の停止、有限二周の再発音を Noise にも適用する。</summary>
        [Fact]
        public void DelayAndFiniteLoopKeepOriginalEndpoints()
        {
            Song song = CreateSong(NoiseMode.Long, FrameTicks * 3);
            song.LoopStartTick = FrameTicks;
            song.Tracks[NoiseTrack].Notes.Add(new Note
            {
                Tick = FrameTicks, DurationTicks = FrameTicks * 2, InstrumentId = NoiseInstrument,
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, FrameTicks) }
            });
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = 2 });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(FrameSamples * 5L, timeline.EndSamples);
            Assert.Equal(new long[] { FrameSamples * 2, FrameSamples * 4 }, timeline.Writes.Where(write => write.Address == NoiseLength).Select(write => write.PositionSamples));
            Assert.Equal(new[] { (Status, 0) }, ValuesAt(timeline, FrameSamples * 3));
        }

        private static Song CreateSong(NoiseMode mode, int lengthTicks)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: lengthTicks);
            song.Instruments.Add(new NesNoiseInstrument { Id = NoiseInstrument, NoiseMode = mode });
            return song;
        }

        private static ControlTimelineResult CreateControl(Song song, bool strict = false)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Strict = strict });

        private static RegisterTimeline Compile(Song song)
        {
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.True(control.Report.CanWrite);
            return timeline;
        }

        private static (int Address, int Value)[] ValuesAt(RegisterTimeline timeline, long positionSamples)
            => timeline.Writes.Where(write => write.PositionSamples == positionSamples)
                .Select(write => ((int)write.Address, (int)write.Value)).ToArray();
    }
}
