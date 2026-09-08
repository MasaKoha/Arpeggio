using System;
using System.IO;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>保存済み NSF の bank 境界・最大容量・終端直前を独立 CPU で検証する。</summary>
    public sealed class NsfBankExecutionTests
    {
        private const int HeaderBytes = 128;
        private const int BankBytes = 4096;
        private const int MaximumDataBytes = 1044480;
        private const int WritesPerFrame = 28;
        private const int MaximumFittingWrites = 336154;

        /// <summary>WRITE 値・WAIT low・命令先頭の三種の 4 KiB 越えを同じ実ファイルで通す。</summary>
        [Fact]
        public void CommandsAndOperandsCrossBanksWithoutChangingTrace()
        {
            const int writeCount = 2100;
            var writes = Enumerable.Range(0, writeCount)
                .Select(index => new NsfRegisterWrite(index, 0x4002, (byte)(index % 256))).ToList();
            writes.Add(new NsfRegisterWrite(writeCount, 0x4015, 0));
            var timeline = new NsfFrameTimeline(writeCount, writes);
            var (bytes, file, report) = NsfWriterFixture.Save(timeline);
            int dataStart = HeaderBytes + BankBytes;
            Assert.Equal(new byte[] { 1, 1, 0 }, bytes.Skip(dataStart + BankBytes - 1).Take(3));
            Assert.Equal(new byte[] { 0, 2, 85 }, bytes.Skip(dataStart + 2 * BankBytes - 2).Take(3));
            Assert.Equal((byte)0, bytes[dataStart + 3 * BankBytes]);
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
            Assert.Equal(new byte[] { 2, 3, 4 }, file.Memory.Writes.Where(write => write.Address == 0x5FF9).Select(write => write.Value));
            Assert.Equal(new long[] { 682, 1365, 2048 }, file.Memory.Writes.Where(write => write.Address == 0x5FF9).Select(write => write.Frame));
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
        }

        /// <summary>WAIT high と WRITE offset が次 bank へ移る配置も固定 byte と実行列で照合する。</summary>
        [Fact]
        public void AlternateCommandOrderCrossesOtherOperandPositions()
        {
            const int writeCount = 1400;
            var writes = Enumerable.Range(0, writeCount)
                .Select(index => new NsfRegisterWrite(index + 1, 0x4015, 0)).ToArray();
            var timeline = new NsfFrameTimeline(writeCount, writes);
            var (bytes, file, report) = NsfWriterFixture.Save(timeline);
            int dataStart = HeaderBytes + BankBytes;
            Assert.Equal(new byte[] { 0, 21, 0 }, bytes.Skip(dataStart + BankBytes - 1).Take(3));
            Assert.Equal(new byte[] { 1, 1, 0 }, bytes.Skip(dataStart + 2 * BankBytes - 2).Take(3));
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
        }

        /// <summary>bank 最終 byte と次 bank 先頭の END を読み、必要な切り替えだけを行う。</summary>
        [Theory]
        [InlineData(1320, 45, 4096, 1)]
        [InlineData(3960, 136, 12289, 4)]
        public void EndAtBankBoundaryDoesNotSwitchEarly(int writeCount, long endFrame, int dataBytes, int lastBank)
        {
            var writes = Enumerable.Range(0, writeCount)
                .Select(index => new NsfRegisterWrite(index / 29, 0x4015, 0)).ToArray();
            var timeline = new NsfFrameTimeline(endFrame, writes);
            var (bytes, file, report) = NsfWriterFixture.Save(timeline);
            Assert.Equal(HeaderBytes + (lastBank + 1) * BankBytes, bytes.Length);
            Assert.Equal((byte)2, bytes[HeaderBytes + BankBytes + dataBytes - 1]);
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
            Assert.Equal(Enumerable.Range(2, lastBank - 1).Select(bank => (byte)bank),
                file.Memory.Writes.Where(write => write.Address == 0x5FF9).Select(write => write.Value));
            Assert.Equal((byte)(lastBank == 1 ? 0xA0 : 0x90), file.Memory.Read(1));
        }

        /// <summary>容量上限直前の実ファイルを全 bank にわたり実行し、追加一命令は符号化前に拒否する。</summary>
        [Fact]
        public void MaximumRomRoundTripsAndNextCommandIsRejected()
        {
            NsfFrameTimeline timeline = CreateMaximumTimeline();
            var (bytes, file, report) = NsfWriterFixture.Save(timeline);
            Assert.Equal(HeaderBytes + 1048576, bytes.Length);
            Assert.Equal(MaximumDataBytes - 2L, report.Statistics["nsfDataBytes"]);
            Assert.Equal(new byte[] { 2, 0, 0 }, bytes.TakeLast(3));
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, report.Statistics["maximumPlayCycles"]);
            Assert.Equal(Enumerable.Range(2, 254).Select(bank => (byte)bank),
                file.Memory.Writes.Where(write => write.Address == 0x5FF9).Select(write => write.Value));
            Assert.Equal((byte)255, file.Memory.Read(2));
            Assert.Equal((byte)0, file.Memory.Read(0x4015));
            var oversizedWrites = timeline.Writes.ToList();
            oversizedWrites.Add(new NsfRegisterWrite(timeline.EndFrame, 0x4015, 0));
            ConversionReport rejected = NsfExecutionFixture.CreateReport();
            Assert.Null(NsfDataEncoder.Encode(new NsfFrameTimeline(timeline.EndFrame, oversizedWrites), rejected));
            Assert.Contains(rejected.Errors, diagnostic => diagnostic.Code == "NsfDataLimitExceeded");
        }

        /// <summary>bank 255 の最終 END は受理し、未知命令とオペランド不足による越境は消音して復帰する。</summary>
        [Theory]
        [InlineData(4095, new byte[] { 2 }, false)]
        [InlineData(4095, new byte[] { 0 }, true)]
        [InlineData(4095, new byte[] { 1 }, true)]
        [InlineData(4095, new byte[] { 0xFF }, true)]
        [InlineData(4094, new byte[] { 0, 21 }, true)]
        [InlineData(4094, new byte[] { 1, 1 }, true)]
        public void LastBankEndAndTruncatedOperandsReturnSafely(int cursor, byte[] tail, bool defensiveStop)
        {
            var (bytes, _, report) = NsfWriterFixture.Save(CreateMaximumTimeline());
            tail.CopyTo(bytes, HeaderBytes + 255 * BankBytes + cursor);
            IndependentNsfLoader file = LoadModifiedFile(bytes);
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            file.Memory.Load(0, (byte)cursor, (byte)(0x90 + cursor / 256), 255, 0, 0, 0);
            file.Memory.Write(0x5FF9, 255, 0);
            file.Memory.Writes.Clear();
            long readsBefore = file.Memory.DataReads;
            Assert.InRange(processor.Call(file.PlayAddress, 8000), 1, report.Statistics["maximumPlayCycles"]);
            Assert.Equal((long)tail.Length, file.Memory.DataReads - readsBefore);
            Assert.Equal((byte)1, file.Memory.Read(5));
            Assert.DoesNotContain(file.Memory.Writes, write => write.Address == 0x5FF9);
            if (defensiveStop)
            {
                var stop = Assert.Single(file.Memory.Writes, write => write.Address is >= 0x4000 and <= 0x4017);
                Assert.Equal((0x4015, 0), ((int)stop.Address, (int)stop.Value));
            }
            else
            {
                Assert.Empty(file.Memory.Writes);
            }
            file.Memory.Writes.Clear();
            processor.Call(file.PlayAddress, 8000);
            Assert.Empty(file.Memory.Writes);
            Assert.Equal((long)tail.Length, file.Memory.DataReads - readsBefore);
        }

        private static NsfFrameTimeline CreateMaximumTimeline()
        {
            var writes = Enumerable.Range(0, MaximumFittingWrites)
                .Select(index => new NsfRegisterWrite(index / WritesPerFrame, 0x4015, 0)).ToArray();
            return new NsfFrameTimeline((MaximumFittingWrites - 1) / WritesPerFrame, writes);
        }

        private static IndependentNsfLoader LoadModifiedFile(byte[] bytes)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "nsf-boundary-" + Guid.NewGuid().ToString("N") + ".nsf");
            try
            {
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    destination.Write(bytes);
                }
                return IndependentNsfLoader.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
