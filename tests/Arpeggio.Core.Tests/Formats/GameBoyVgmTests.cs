using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using static Arpeggio.Core.Tests.Formats.GameBoyRegisterTestData;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>GB 四声のレジスタ列を VGM へ接続し、独立パーサーによる完全往復と固定バイト値を検証する。</summary>
    public sealed class GameBoyVgmTests
    {
        private const int NoiseInstrumentId = 3;
        private const int HeaderBytes = 256;
        private const int GameBoyClockField = 0x80;
        private const int CommandBytes = 3;
        private const int TriggerMask = 0x80;
        private const int Routing = 0xFF25;
        private const int NoiseTrigger = 0xFF23;

        /// <summary>空曲の全ヘッダーと命令列を固定 byte 値で検証し、全四声停止の直後へ END／GD3 を置く。</summary>
        [Fact]
        public void EmptyGameBoySongHasExactHeaderAndCommands()
        {
            const int FileBytes = 386;
            const int Gd3Start = 302;
            Song song = CreateSong(FrameTicks);
            song.Title = string.Empty;
            (RegisterTimeline timeline, ConversionReport report) = CompileGameBoy(song);
            byte[] bytes = VgmWriterTests.Write(timeline, song.Title, report);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            var expectedHeader = new byte[HeaderBytes];
            byte[] initialHeader = { 0x56, 0x67, 0x6D, 0x20, 0x7E, 0x01, 0, 0, 0x71, 0x01, 0, 0 };
            Array.Copy(initialHeader, expectedHeader, initialHeader.Length);
            expectedHeader[0x14] = 0x1A;
            expectedHeader[0x15] = 0x01;
            expectedHeader[0x18] = 0xDF;
            expectedHeader[0x19] = 0x02;
            expectedHeader[0x34] = 0xCC;
            expectedHeader[GameBoyClockField + 2] = 0x40;
            Assert.Equal(expectedHeader, bytes.Take(HeaderBytes));
            Assert.Equal(new byte[]
            {
                0xB3, 0x16, 0, 0xB3, 0x16, 0x80, 0xB3, 0x14, 0x77, 0xB3, 0x15, 0, 0xB3, 0, 0,
                0x61, 0xDF, 0x02,
                0xB3, 0x02, 0, 0xB3, 0x07, 0, 0xB3, 0x0A, 0, 0xB3, 0x11, 0,
                0xB3, 0x15, 0, 0xB3, 0x02, 0, 0xB3, 0x07, 0, 0xB3, 0x11, 0, 0xB3, 0x0A, 0, 0x66
            }, bytes.Skip(HeaderBytes).Take(Gd3Start - HeaderBytes));
            Assert.Equal(FileBytes, bytes.Length);
            Assert.Equal(Gd3Start, parsed.Gd3Start);
            Assert.Equal(72U, parsed.Gd3PayloadBytes);
            VgmWriterTests.AssertRoundTrip(timeline, parsed);
        }

        /// <summary>先頭無音・四声の同時交代・時間 envelope・Noise 復帰・有限二周と日本語 GD3 を完全往復する。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void FourVoiceSongRoundTripsEveryWriteAndFiniteLoop(int loops)
        {
            const int SongTicks = FrameTicks * 5;
            const int NextTick = FrameTicks * 2;
            const string Title = "月とノイズ";
            const string Author = "作曲者";
            Song song = CreateSong(SongTicks);
            song.Title = Title;
            song.LoopStartTick = FrameTicks;
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).EnvelopeStepFrames = 1;
            Assert.IsType<GbWaveInstrument>(song.Instruments[1]).Waveform = Enumerable.Range(0, 32).Select(value => value % 16).ToArray();
            song.Instruments.Add(new GbNoiseInstrument
            {
                Id = NoiseInstrumentId, LfsrWidth = 7,
                VolumeMacro = new Macro { Values = new[] { 15, 0, 9 } }
            });
            for (int trackIndex = PulseOneTrack; trackIndex <= NoiseTrack; trackIndex++)
            {
                int selection = trackIndex == NoiseTrack ? 120 : ConcertNote;
                Note first = AddNote(song, trackIndex, FrameTicks, FrameTicks, selection);
                Note next = AddNote(song, trackIndex, NextTick, SongTicks - NextTick, selection);
                if (trackIndex == NoiseTrack)
                {
                    first.InstrumentId = NoiseInstrumentId;
                    next.InstrumentId = NoiseInstrumentId;
                }
            }
            string originalJson = SongSerializer.Serialize(song);
            (RegisterTimeline timeline, ConversionReport report) = CompileGameBoy(song, loops);
            long? expectedBytes = VgmWriter.CalculateSize(timeline, song.Title, Author, report);
            byte[] bytes = VgmWriterTests.Write(timeline, song.Title, report, Author);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            VgmWriterTests.AssertRoundTrip(timeline, parsed);
            Assert.Equal(bytes, VgmWriterTests.Write(timeline, song.Title, report, Author));
            Assert.Equal(originalJson, SongSerializer.Serialize(song));
            Assert.Equal((long)bytes.Length, expectedBytes);
            Assert.Equal(4194304U, parsed.GameBoyClock);
            Assert.Equal(0U, parsed.NesClock);
            Assert.Equal(new[] { "", Title, "", "", "Nintendo Game Boy", "", "", Author, "", "Arpeggio", "" }, parsed.Gd3Fields);
            Assert.Equal(FrameSamples, parsed.Waits[0]);
            Assert.Equal((long)(SongTicks + (loops - 1) * (SongTicks - FrameTicks)) * FrameSamples / FrameTicks, parsed.WaitSamples);
            Assert.Equal(new[]
            {
                (0xFF12, 0), (Routing, 0xEE), (0xFF17, 0), (Routing, 0xCC),
                (0xFF1A, 0), (Routing, 0x88), (0xFF21, 0), (Routing, 0)
            }, parsed.Writes.Where(write => write.Sample == FrameSamples * 2).Take(8).Select(write => (write.Address, write.Value)));
            Assert.Equal(loops * 4, parsed.Writes.Count(write => write.Address == 0xFF14 && (write.Value & TriggerMask) != 0));
            Assert.Equal(loops * 4, parsed.Writes.Count(write => write.Address == 0xFF19 && (write.Value & TriggerMask) != 0));
            Assert.Equal(loops * 2, parsed.Writes.Count(write => write.Address == 0xFF1E && (write.Value & TriggerMask) != 0));
            Assert.Equal(loops * 3, parsed.Writes.Count(write => write.Address == NoiseTrigger));
            Assert.Equal(new[] { (Routing, 0), (0xFF12, 0), (0xFF17, 0), (0xFF21, 0), (0xFF1A, 0) },
                parsed.Writes.TakeLast(5).Select(write => (write.Address, write.Value)));
            Assert.All(parsed.Writes.TakeLast(5), write => Assert.Equal(timeline.EndSamples, write.Sample));
            Assert.Equal(parsed.EndCommandPosition + 1, parsed.Gd3Start);
            Assert.Equal(0x66, bytes[parsed.EndCommandPosition]);
            AssertWaveRamCommands(bytes, parsed);
        }

        /// <summary>GB Noise の開始までの長待機を 65535 以下へ分割し、絶対時刻を維持する。</summary>
        [Fact]
        public void LongLeadingSilenceSplitsWaitAndPreservesNoiseOnset()
        {
            const int StartTick = 143;
            const int OnsetSample = 65691;
            Song song = SongFactory.Create(ChipKind.GameBoy, tempoBpm: 120, lengthTicks: StartTick + 2);
            song.Instruments.Add(new GbNoiseInstrument { Id = NoiseInstrumentId });
            AddNote(song, NoiseTrack, StartTick, 2, 120).InstrumentId = NoiseInstrumentId;
            (RegisterTimeline timeline, ConversionReport report) = CompileGameBoy(song);
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
            VgmWriterTests.AssertRoundTrip(timeline, parsed);
            Assert.Equal(new[] { 65535, OnsetSample - 65535 }, parsed.Waits.Take(2));
            Assert.Equal(OnsetSample, Assert.Single(parsed.Writes, write => write.Address == NoiseTrigger).Sample);
        }

        /// <summary>GB の保存も Seek 不要で Stream の所有権を保ち、Noise 警告を持つ strict レポートでは出力しない。</summary>
        [Fact]
        public void GameBoyStreamOwnershipAndStrictDiagnosticsArePreserved()
        {
            Song song = CreateSong();
            song.Instruments.Add(new GbNoiseInstrument { Id = NoiseInstrumentId });
            AddNote(song, NoiseTrack, 0, song.LengthTicks, 1).InstrumentId = NoiseInstrumentId;
            (RegisterTimeline timeline, ConversionReport report) = CompileGameBoy(song);
            using var destination = new VgmTestStream();
            Assert.True(VgmWriter.Write(destination, timeline, song.Title, string.Empty, report));
            Assert.True(destination.CanWrite);
            VgmWriterTests.AssertRoundTrip(timeline, IndependentVgmParser.Parse(destination.GetWrittenBytes()));
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var strictReport = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true, diagnosticDetailLimit: 0);
            Assert.Null(GameBoyRegisterCompiler.Compile(control.Timeline, strictReport));
            using var rejected = new MemoryStream();
            Assert.False(VgmWriter.Write(rejected, timeline, song.Title, string.Empty, strictReport));
            Assert.Empty(rejected.ToArray());
            Assert.True(rejected.CanWrite);
        }

        /// <summary>writer を使わない手書き GB ファイルで B3 の基準アドレスと Wave RAM 両端を独立に読む。</summary>
        [Fact]
        public void HandWrittenGameBoyFixtureDecodesWaveRamOffsets()
        {
            const int FixtureBytes = 300;
            const int Gd3Start = 266;
            var bytes = new byte[FixtureBytes];
            byte[] prefix = { 0x56, 0x67, 0x6D, 0x20, 0x28, 0x01, 0, 0, 0x71, 0x01, 0, 0 };
            Array.Copy(prefix, bytes, prefix.Length);
            bytes[0x14] = 0xF6;
            bytes[0x18] = 1;
            bytes[0x34] = 0xCC;
            bytes[GameBoyClockField + 2] = 0x40;
            byte[] commands = { 0xB3, 0x20, 0x01, 0x61, 1, 0, 0xB3, 0x2F, 0xEF, 0x66 };
            Array.Copy(commands, 0, bytes, HeaderBytes, commands.Length);
            byte[] tagHeader = { 0x47, 0x64, 0x33, 0x20, 0, 1, 0, 0, 22, 0, 0, 0 };
            Array.Copy(tagHeader, 0, bytes, Gd3Start, tagHeader.Length);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(new[] { (0L, 0xFF30, 0x01, 0), (1L, 0xFF3F, 0xEF, 1) }, parsed.Writes);
            bytes[HeaderBytes] = 0xB4;
            Assert.Throws<InvalidDataException>(() => IndependentVgmParser.Parse(bytes));
        }

        private static (RegisterTimeline Timeline, ConversionReport Report) CompileGameBoy(Song song, int loops = 1)
        {
            ControlTimelineResult control = CreateControl(song, loops);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            return (timeline, control.Report);
        }

        private static void AssertWaveRamCommands(byte[] bytes, ParsedVgm parsed)
        {
            int commandPosition = HeaderBytes;
            int writeIndex = 0;
            while (commandPosition < parsed.EndCommandPosition)
            {
                if (bytes[commandPosition] == 0xB3)
                {
                    var write = parsed.Writes[writeIndex++];
                    if (write.Address == 0xFF30 || write.Address == 0xFF3F)
                    {
                        Assert.Equal(write.Address == 0xFF30 ? 0x20 : 0x2F, bytes[commandPosition + 1]);
                        Assert.Equal(write.Address == 0xFF30 ? 0x01 : 0xEF, bytes[commandPosition + 2]);
                    }
                }
                commandPosition += CommandBytes;
            }
        }
    }
}
