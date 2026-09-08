using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>独立したゲートモデルで四声の副作用と有限終端を検証する。</summary>
    public sealed class NesRegisterTerminationTests
    {
        private const int PulseOneTrack = 0;
        private const int PulseTwoTrack = 1;
        private const int TriangleTrack = 2;
        private const int NoiseTrack = 3;
        private const int TriangleInstrument = 2;
        private const int NoiseInstrument = 3;
        private const int FrameTicks = 2;
        private const int FrameSamples = 735;
        private const int LengthCounterValue = 10;
        private const int Status = 0x4015;
        private const int PulseOneControl = 0x4000;
        private const int PulseTwoControl = 0x4004;
        private const int NoiseControl = 0x400C;
        private const int TriangleHigh = 0x400B;
        private const int FrameCounter = 0x4017;

        /// <summary>四声が有効化後に length をロードし、個別 Off で他声のカウンターを失わない。</summary>
        [Theory]
        [InlineData(NoiseMode.Long, false)]
        [InlineData(NoiseMode.Short, true)]
        public void IndependentGatesPreserveOtherChannelsUntilFiniteEnd(NoiseMode mode, bool expectedShort)
        {
            Song song = CreateSong();
            Assert.IsType<NesNoiseInstrument>(song.Instruments[NoiseInstrument - 1]).NoiseMode = mode;
            AddNote(song, PulseOneTrack, FrameTicks);
            AddNote(song, PulseTwoTrack, FrameTicks * 3);
            AddNote(song, TriangleTrack, song.LengthTicks);
            AddNote(song, NoiseTrack, FrameTicks * 2);
            RegisterTimeline timeline = Compile(song);
            var model = new NesRegisterGateModel();
            ApplyAt(model, timeline, 0);
            Assert.Equal(0x0F, model.EnabledChannels);
            for (int channel = PulseOneTrack; channel <= NoiseTrack; channel++)
            {
                Assert.Equal(LengthCounterValue, model.Length(channel));
                Assert.True(model.IsGated(channel));
            }
            Assert.Equal(0x7F, model.TriangleLinear);
            Assert.Equal(expectedShort, model.NoiseShort);
            Assert.Equal(5, model.NoisePeriod);
            ApplyAt(model, timeline, FrameSamples);
            Assert.Equal(0x0E, model.EnabledChannels);
            Assert.False(model.IsGated(PulseOneTrack));
            Assert.Equal(LengthCounterValue, model.Length(NoiseTrack));
            ApplyAt(model, timeline, FrameSamples * 2);
            Assert.Equal(0x06, model.EnabledChannels);
            Assert.False(model.IsGated(NoiseTrack));
            Assert.True(model.IsGated(PulseTwoTrack));
            Assert.True(model.IsGated(TriangleTrack));
            ApplyAt(model, timeline, FrameSamples * 3);
            Assert.Equal(0x04, model.EnabledChannels);
            ApplyAt(model, timeline, timeline.EndSamples);
            Assert.Equal(0, model.EnabledChannels);
            for (int channel = PulseOneTrack; channel <= NoiseTrack; channel++)
            {
                Assert.False(model.IsGated(channel));
            }
            Assert.Equal(0, model.Volume(PulseOneTrack));
            Assert.Equal(0, model.Volume(PulseTwoTrack));
            Assert.Equal(0, model.Volume(NoiseTrack));
            Assert.Equal(0x7F, model.TriangleLinear);
        }

        /// <summary>Triangle は high の reload flag だけでは開始せず、後続の即時クロックで linear をロードする。</summary>
        [Fact]
        public void TriangleLinearLoadsAfterLengthAndImmediateFrameClock()
        {
            Song song = CreateSong();
            AddNote(song, TriangleTrack, song.LengthTicks);
            RegisterTimeline timeline = Compile(song);
            var model = new NesRegisterGateModel();
            foreach (RegisterWrite write in timeline.Writes.Where(write => write.PositionSamples == 0))
            {
                model.Apply(write);
                if (write.Address == TriangleHigh)
                {
                    Assert.Equal(LengthCounterValue, model.Length(TriangleTrack));
                    Assert.Equal(0, model.TriangleLinear);
                    Assert.False(model.IsGated(TriangleTrack));
                }
                if (write.Address == FrameCounter && model.Length(TriangleTrack) > 0)
                {
                    Assert.True(model.IsGated(TriangleTrack));
                }
            }
            Assert.True(model.IsGated(TriangleTrack));
        }

        /// <summary>同時交代は四声すべてを Off してから再開し、同値 On でも各 length を再ロードする。</summary>
        [Fact]
        public void FourChannelReplacementPreservesAllOffBeforeOnAndReloads()
        {
            Song song = CreateSong();
            for (int trackIndex = PulseOneTrack; trackIndex <= NoiseTrack; trackIndex++)
            {
                AddNote(song, trackIndex, FrameTicks);
                Note second = new Note
                {
                    Tick = FrameTicks, DurationTicks = FrameTicks, MidiNote = 69,
                    InstrumentId = InstrumentFor(trackIndex)
                };
                song.Tracks[trackIndex].Notes.Add(second);
            }
            RegisterTimeline timeline = Compile(song);
            RegisterWrite[] replacement = timeline.Writes.Where(write => write.PositionSamples == FrameSamples).ToArray();
            Assert.Equal(new byte[] { 14, 12, 8, 0 }, replacement.Take(4).Select(write => write.Value));
            Assert.All(replacement.Take(4), write => Assert.Equal(Status, write.Address));
            var model = new NesRegisterGateModel();
            ApplyAt(model, timeline, 0);
            ApplyAt(model, timeline, FrameSamples);
            Assert.Equal(0x0F, model.EnabledChannels);
            for (int channel = PulseOneTrack; channel <= NoiseTrack; channel++)
            {
                Assert.Equal(2, model.Loads(channel));
                Assert.True(model.IsGated(channel));
            }
            Assert.Equal(4, model.PulsePhaseRestarts);
        }

        /// <summary>空曲・先に停止済みの曲・終端まで発音する曲のすべてで、全停止と三つの volume=0 を末尾に書く。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(FrameTicks)]
        [InlineData(FrameTicks * 4)]
        public void EveryTimelineEndsWithExplicitStatusAndZeroVolumes(int noteDuration)
        {
            Song song = CreateSong();
            if (noteDuration > 0)
            {
                for (int trackIndex = PulseOneTrack; trackIndex <= NoiseTrack; trackIndex++)
                {
                    AddNote(song, trackIndex, noteDuration);
                }
            }
            RegisterTimeline timeline = Compile(song);
            RegisterWrite[] terminal = timeline.Writes.TakeLast(4).ToArray();
            Assert.All(terminal, write => Assert.Equal(timeline.EndSamples, write.PositionSamples));
            int pulseSilence = noteDuration == 0 ? 0x30 : 0xB0;
            Assert.Equal(new[] { (Status, 0), (PulseOneControl, pulseSilence), (PulseTwoControl, pulseSilence), (NoiseControl, 0x30) },
                terminal.Select(write => ((int)write.Address, (int)write.Value)));
            Assert.Equal(FrameSamples * 4L, timeline.EndSamples);
            Assert.Equal(Enumerable.Range(0, timeline.Writes.Count), timeline.Writes.Select(write => write.Order));
        }

        private static Song CreateSong()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: FrameTicks * 4);
            song.Instruments.Add(new NesTriangleInstrument { Id = TriangleInstrument });
            song.Instruments.Add(new NesNoiseInstrument { Id = NoiseInstrument });
            return song;
        }

        private static int InstrumentFor(int trackIndex)
            => trackIndex switch { TriangleTrack => TriangleInstrument, NoiseTrack => NoiseInstrument, _ => 1 };

        private static void AddNote(Song song, int trackIndex, int durationTicks)
            => song.Tracks[trackIndex].Notes.Add(new Note { DurationTicks = durationTicks, MidiNote = 69, InstrumentId = InstrumentFor(trackIndex) });

        private static RegisterTimeline Compile(Song song)
        {
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            return timeline;
        }

        private static void ApplyAt(NesRegisterGateModel model, RegisterTimeline timeline, long positionSamples)
        {
            foreach (RegisterWrite write in timeline.Writes.Where(write => write.PositionSamples == positionSamples))
            {
                model.Apply(write);
            }
        }
    }
}
