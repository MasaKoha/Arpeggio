using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>NES の VGM を独立パーサーで戻し、時刻・順序・固定バイト値を照合する。</summary>
    public sealed class VgmWriterTests
    {
        private const int PulseOneTrack = 0;
        private const int TriangleTrack = 2;
        private const int NoiseTrack = 3;
        private const int TriangleInstrument = 2;
        private const int NoiseInstrument = 3;
        private const int ConcertNote = 69;
        private const int FrameTicks = 2;
        private const int FrameSamples = 735;
        private const int HeaderBytes = 256;
        private const int Gd3Field = 0x14;
        private const int DataField = 0x34;
        private const int NesClockField = 0x84;
        private const int InitializationBytes = 18;

        /// <summary>空曲も停止列を持ち、ヘッダー全 256 byte と初期化・待機・GD3 の位置が固定値に一致する。</summary>
        [Fact]
        public void EmptySongHasExactHeaderAndCommandBytes()
        {
            const int FileBytes = 410;
            const int Gd3Start = 302;
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: FrameTicks);
            (RegisterTimeline timeline, ConversionReport report) = Compile(song);
            byte[] bytes = Write(timeline, song.Title, report);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(FileBytes, bytes.Length);
            Assert.Equal(Gd3Start, parsed.Gd3Start);
            Assert.Equal(new byte[] { 0x56, 0x67, 0x6D, 0x20, 0x96, 0x01, 0x00, 0x00, 0x71, 0x01, 0x00, 0x00 }, bytes.Take(12));
            var expectedHeader = new byte[HeaderBytes];
            Array.Copy(new byte[] { 0x56, 0x67, 0x6D, 0x20, 0x96, 0x01, 0, 0, 0x71, 0x01, 0, 0 }, expectedHeader, 12);
            Array.Copy(new byte[] { 0x1A, 0x01, 0, 0, 0xDF, 0x02, 0, 0 }, 0, expectedHeader, Gd3Field, 8);
            expectedHeader[DataField] = 0xCC;
            Array.Copy(new byte[] { 0x4D, 0x4F, 0x1B, 0 }, 0, expectedHeader, NesClockField, 4);
            Assert.Equal(expectedHeader, bytes.Take(HeaderBytes));
            Assert.Equal(new byte[]
            {
                0xB4, 0x15, 0x00, 0xB4, 0x10, 0x00, 0xB4, 0x11, 0x00,
                0xB4, 0x01, 0x08, 0xB4, 0x05, 0x08, 0xB4, 0x17, 0xC0,
                0x61, 0xDF, 0x02,
                0xB4, 0x15, 0, 0xB4, 0x15, 0, 0xB4, 0x15, 0, 0xB4, 0x15, 0,
                0xB4, 0x15, 0, 0xB4, 0x00, 0x30, 0xB4, 0x04, 0x30, 0xB4, 0x0C, 0x30, 0x66
            }, bytes.Skip(HeaderBytes).Take(Gd3Start - HeaderBytes));
            Assert.Equal(0x171U, parsed.Version);
            Assert.Equal(1789773U, parsed.NesClock);
            Assert.Equal(0U, parsed.GameBoyClock);
            Assert.Equal(FrameSamples, parsed.WaitSamples);
            Assert.Equal(parsed.Gd3Start - 1, parsed.EndCommandPosition);
            Assert.Equal((long)bytes.Length, report.OutputBytes);
            Assert.Equal(FrameSamples / 44100.0, report.DurationSeconds);
            AssertRoundTrip(timeline, parsed);
        }

        /// <summary>待機 1／65535／65536 と複数回分割のバイト列・総和・終端を絶対サンプル位置で検証する。</summary>
        [Theory]
        [InlineData(55125, 1, 1, new int[] { 1 }, new byte[] { 0x61, 0x01, 0x00 })]
        [InlineData(233, 277, 65535, new int[] { 65535 }, new byte[] { 0x61, 0xFF, 0xFF })]
        [InlineData(323, 384, 65536, new int[] { 65535, 1 }, new byte[] { 0x61, 0xFF, 0xFF, 0x61, 0x01, 0x00 })]
        [InlineData(233, 554, 131070, new int[] { 65535, 65535 }, new byte[] { 0x61, 0xFF, 0xFF, 0x61, 0xFF, 0xFF })]
        [InlineData(278, 661, 131071, new int[] { 65535, 65535, 1 }, new byte[] { 0x61, 0xFF, 0xFF, 0x61, 0xFF, 0xFF, 0x61, 0x01, 0x00 })]
        public void WaitBoundariesUsePositiveLittleEndianChunks(int tempo, int ticks, long expectedSamples,
            int[] expectedWaits, byte[] expectedBytes)
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: tempo, lengthTicks: ticks);
            (RegisterTimeline timeline, ConversionReport report) = Compile(song);
            Assert.Equal(expectedSamples, timeline.EndSamples);
            byte[] bytes = Write(timeline, song.Title, report);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(expectedWaits, parsed.Waits);
            Assert.Equal(expectedBytes, bytes.Skip(HeaderBytes + InitializationBytes).Take(expectedBytes.Length));
            Assert.Equal(expectedSamples, parsed.WaitSamples);
            Assert.All(parsed.Writes.Skip(InitializationBytes / 3), write => Assert.Equal(expectedSamples, write.Sample));
            AssertRoundTrip(timeline, parsed);
        }

        /// <summary>マクロ更新と直後の終端の 1 サンプル差を消さず、ゼロ待機を挟まない。</summary>
        [Fact]
        public void OneSampleBetweenMacroUpdateAndStopIsPreserved()
        {
            const int Tempo = 524;
            const int SongTicks = 7;
            const int EndSamples = 736;
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: Tempo, lengthTicks: SongTicks);
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).VolumeMacro = new Macro { Values = new[] { 15, 14 } };
            song.Tracks[PulseOneTrack].Notes.Add(new Note { DurationTicks = SongTicks, MidiNote = ConcertNote });
            (RegisterTimeline timeline, ConversionReport report) = Compile(song);
            ParsedVgm parsed = IndependentVgmParser.Parse(Write(timeline, song.Title, report));
            Assert.Equal(new[] { FrameSamples, 1 }, parsed.Waits);
            Assert.Contains(parsed.Writes, write => write.Sample == FrameSamples && write.Address == 0x4000 && write.Value == 0xBE);
            Assert.Equal(EndSamples, parsed.WaitSamples);
            AssertRoundTrip(timeline, parsed);
        }

        /// <summary>先頭無音・四声の同時交代・再トリガー・有限二周・終端停止まで全タプルが一致する。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void FourVoicesAndFiniteLoopsRoundTripEveryWrite(int loops)
        {
            const int SongTicks = 8;
            const int FirstTick = 2;
            const int NextTick = 4;
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: SongTicks);
            song.LoopStartTick = FirstTick;
            song.Instruments.Add(new NesTriangleInstrument { Id = TriangleInstrument });
            song.Instruments.Add(new NesNoiseInstrument { Id = NoiseInstrument, NoiseMode = NoiseMode.Short });
            for (int trackIndex = PulseOneTrack; trackIndex <= NoiseTrack; trackIndex++)
            {
                int instrument = trackIndex == TriangleTrack ? TriangleInstrument : 1;
                if (trackIndex == NoiseTrack)
                {
                    instrument = NoiseInstrument;
                }
                song.Tracks[trackIndex].Notes.Add(new Note { Tick = FirstTick, DurationTicks = FrameTicks, MidiNote = ConcertNote, InstrumentId = instrument });
                song.Tracks[trackIndex].Notes.Add(new Note { Tick = NextTick, DurationTicks = SongTicks - NextTick, MidiNote = ConcertNote, InstrumentId = instrument });
            }
            (RegisterTimeline timeline, ConversionReport report) = Compile(song, loops);
            byte[] first = Write(timeline, song.Title, report);
            ParsedVgm parsed = IndependentVgmParser.Parse(first);
            AssertRoundTrip(timeline, parsed);
            Assert.Equal(first, Write(timeline, song.Title, report));
            Assert.Equal(FrameSamples, parsed.Waits[0]);
            Assert.Equal((long)(SongTicks + (loops - 1) * (SongTicks - FirstTick)) * FrameSamples / FrameTicks, parsed.WaitSamples);
            Assert.Equal(new[] { (0x4015, 0), (0x4000, 0xB0), (0x4004, 0xB0), (0x400C, 0x30) },
                parsed.Writes.TakeLast(4).Select(write => (write.Address, write.Value)));
            Assert.All(parsed.Writes.TakeLast(4), write => Assert.Equal(timeline.EndSamples, write.Sample));
            Assert.Equal(new[] { 0x0E, 0x0C, 0x08, 0x00 }, parsed.Writes.Where(write => write.Sample == FrameSamples * 2).Take(4).Select(write => write.Value));
            Assert.Equal(loops * 2, parsed.Writes.Count(write => write.Address == 0x4003));
            Assert.Equal(loops * 2, parsed.Writes.Count(write => write.Address == 0x400F));
        }

        /// <summary>最大演奏長の空曲でも長待機の総和を失わず、事前サイズと実出力が一致する。</summary>
        [Fact]
        public void MaximumDurationUsesFiniteWaitsWithoutPcm()
        {
            const int MaximumDurationTicks = 1440;
            const long ExpectedSamples = 79380000;
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 1, lengthTicks: MaximumDurationTicks);
            (RegisterTimeline timeline, ConversionReport report) = Compile(song);
            long? expectedBytes = VgmWriter.CalculateSize(timeline, song.Title, string.Empty, report);
            byte[] bytes = Write(timeline, song.Title, report);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal((long)bytes.Length, expectedBytes);
            Assert.Equal(ExpectedSamples, parsed.WaitSamples);
            AssertRoundTrip(timeline, parsed);
        }

        internal static (RegisterTimeline Timeline, ConversionReport Report) Compile(Song song, int loops = 1)
        {
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            return (timeline, control.Report);
        }

        internal static byte[] Write(RegisterTimeline timeline, string title, ConversionReport report, string author = "")
        {
            using var destination = new MemoryStream();
            Assert.True(VgmWriter.Write(destination, timeline, title, author, report));
            Assert.True(destination.CanWrite);
            return destination.ToArray();
        }

        internal static void AssertRoundTrip(RegisterTimeline timeline, ParsedVgm parsed)
        {
            Assert.Equal(timeline.Writes.Select(write => (write.PositionSamples, (int)write.Address, (int)write.Value, write.Order)), parsed.Writes);
            Assert.Equal(timeline.EndSamples, parsed.WaitSamples);
            Assert.Equal(timeline.EndSamples, (long)parsed.HeaderSamples);
        }
    }
}
