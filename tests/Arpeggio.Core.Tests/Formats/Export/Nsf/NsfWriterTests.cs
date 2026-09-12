using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nsf;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>実ファイルの独立ロードで NSF v1 の全ヘッダー・配置・演奏トレースを検証する。</summary>
    public sealed class NsfWriterTests
    {
        /// <summary>全 128 byte の固定ヘッダー、生成コードの配置、末尾ゼロ埋めと予定サイズが一致する。</summary>
        [Fact]
        public void CompleteHeaderAndBankPaddingHaveFixedBytes()
        {
            var timeline = new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>());
            var (bytes, file, report) = NsfWriterFixture.Save(timeline);
            var expectedHeader = new byte[128];
            new byte[] { 0x4E, 0x45, 0x53, 0x4D, 0x1A, 1, 1, 1, 0, 0x80, 0, 0x80, 0x64, 0x80 }.CopyTo(expectedHeader, 0);
            expectedHeader[0x6E] = 0xFF;
            expectedHeader[0x6F] = 0x40;
            expectedHeader[0x71] = 1;
            expectedHeader[0x78] = 0x1D;
            expectedHeader[0x79] = 0x4E;
            Assert.Equal(expectedHeader, bytes.Take(128));
            Assert.Equal(8320, bytes.Length);
            Assert.Equal((long)bytes.Length, report.OutputBytes);
            var (_, image) = NsfExecutionFixture.Build(timeline);
            Assert.Equal(image.Bank, bytes.Skip(128).Take(4096));
            Assert.Equal((byte)2, bytes[128 + 4096]);
            Assert.All(bytes.Skip(128 + 4096 + 1), value => Assert.Equal((byte)0, value));
            var processor = new Limited6502(file.Memory);
            Assert.InRange(processor.Call(file.InitAddress, 20000), 1, report.Statistics["maximumInitCycles"]);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
            Assert.Equal(bytes, NsfWriterFixture.Save(timeline).Bytes);
        }

        /// <summary>WAIT の固定 byte 列と実 PLAY 番号を、先頭無音・16 bit 上限の両側で照合する。</summary>
        [Theory]
        [InlineData(1, new byte[] { 1, 1, 0, 0, 21, 0, 2 })]
        [InlineData(65535, new byte[] { 1, 255, 255, 0, 21, 0, 2 })]
        [InlineData(65536, new byte[] { 1, 255, 255, 1, 1, 0, 0, 21, 0, 2 })]
        public void SavedWaitsHaveFixedBytesAndPlaybackFrames(long wait, byte[] expected)
        {
            var timeline = new NsfFrameTimeline(wait, new[] { new NsfRegisterWrite(wait, 0x4015, 0) });
            var (bytes, file, report) = NsfWriterFixture.Save(timeline);
            Assert.Equal(expected, bytes.Skip(128 + 4096).Take(expected.Length));
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
        }

        /// <summary>四声・先頭無音・同値再 trigger・有限二周を制御列から実ファイルへ通し、量子化後の期待列へ完全一致する。</summary>
        [Fact]
        public void FourVoiceSongRoundTripsThroughFileAndRestartsAfterReinit()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: 12);
            song.Title = "Four voices";
            song.LoopStartTick = 4;
            song.Instruments.Add(new NesTriangleInstrument { Id = 2, Name = "triangle" });
            song.Instruments.Add(new NesNoiseInstrument { Id = 3, Name = "noise" });
            for (int trackIndex = 0; trackIndex < 4; trackIndex++)
            {
                int instrumentId = trackIndex < 2 ? 1 : trackIndex;
                song.Tracks[trackIndex].Notes.Add(new Note { Tick = 2, DurationTicks = 2, MidiNote = 69, InstrumentId = instrumentId });
                song.Tracks[trackIndex].Notes.Add(new Note { Tick = 4, DurationTicks = 4, MidiNote = 69, InstrumentId = instrumentId });
            }
            ControlTimelineResult source = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Nsf, Loops = 2 });
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? timeline = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(timeline);
            var (_, file, report) = NsfWriterFixture.Save(timeline, source.Timeline.Title);
            Assert.Equal("Four voices", file.Title);
            Assert.Equal(new[] { (0x4015, 1), (0x4000, 0xBF), (0x4002, 0xFD), (0x4003, 0) },
                timeline.Writes.Where(write => write.Frame == 1).Take(4).Select(write => ((int)write.Address, (int)write.Value)));
            Assert.Contains(timeline.Writes, write => write.Address == 0x400B && write.Value == 0);
            Assert.Contains(timeline.Writes, write => write.Address == 0x400F);
            var processor = new Limited6502(file.Memory);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                processor.Call(file.InitAddress, 20000);
                NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
                Assert.Equal((byte)0, file.Memory.Read(0x4015));
            }
        }
    }
}
