using System.Linq;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Nsf;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>生成した INIT／PLAY の実バイトを実行し、値・順序・復帰・サイクル上限を検証する。</summary>
    public sealed class NsfDriverExecutionTests
    {
        /// <summary>INIT は呼び出し元の SP を保持して予約 RAM・APU・bank を復元する。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(0x80)]
        [InlineData(0xFF)]
        public void InitAndReinitRestoreMemoryBankAndApu(byte stackPointer)
        {
            var (data, image) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, new[] { new NsfRegisterWrite(0, 0x4015, 0) }));
            Limited6502Memory memory = NsfExecutionFixture.Map(image, data.Bytes.ToArray());
            var processor = new Limited6502(memory) { StackPointer = stackPointer, Status = 0xFF };
            for (int attempt = 0; attempt < 2; attempt++)
            {
                memory.Load(0, Enumerable.Repeat((byte)0xCC, 0x40).ToArray());
                memory.Write(0x5FF9, 0xFF, 0);
                memory.Writes.Clear();
                Assert.Equal(146L, processor.Call(image.InitAddress, 20000));
                Assert.Equal(stackPointer, processor.StackPointer);
                Assert.Equal(new[] { (0x4015, 0), (0x4010, 0), (0x4011, 0), (0x4001, 8), (0x4005, 8), (0x4017, 0xC0) },
                    memory.Writes.Where(write => write.Address is >= 0x4000 and <= 0x4017)
                        .Select(write => ((int)write.Address, (int)write.Value)));
                Assert.Equal((byte)1, Assert.Single(memory.Writes, write => write.Address == 0x5FF9).Value);
                Assert.Equal(new byte[] { 0, 0x90, 1, 0, 0, 0 }, Enumerable.Range(0, 6).Select(address => memory.Read((ushort)address)));
                Assert.All(Enumerable.Range(6, 26), address => Assert.Equal((byte)0, memory.Read((ushort)address)));
                Assert.Equal((byte)0xCC, memory.Read(0x20));
                Assert.InRange(processor.Cycles, 1, image.MaximumInitCycles);
                NsfExecutionFixture.AssertPlayback(processor, memory, image.PlayAddress,
                    new NsfFrameTimeline(0, new[] { new NsfRegisterWrite(0, 0x4015, 0) }), image.MaximumPlayCycles);
            }
        }

        /// <summary>最初の PLAY は時刻 0、長待機は正しい番号で再開し、END 後はデータを読まない。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(256)]
        [InlineData(65535)]
        [InlineData(65536)]
        public void WaitCountdownAndEndMatchExpectedFrames(long wait)
        {
            var timeline = new NsfFrameTimeline(wait, new[]
            {
                new NsfRegisterWrite(0, 0x4015, 1), new NsfRegisterWrite(0, 0x4000, 0xBF),
                new NsfRegisterWrite(0, 0x4002, 0xFD), new NsfRegisterWrite(0, 0x4003, 0),
                new NsfRegisterWrite(wait, 0x4015, 0)
            });
            var (data, image) = NsfExecutionFixture.Build(timeline);
            Limited6502Memory memory = NsfExecutionFixture.Map(image, data.Bytes.ToArray());
            var processor = new Limited6502(memory);
            processor.Call(image.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, memory, image.PlayAddress, timeline, image.MaximumPlayCycles);
            Assert.Equal((long)data.Bytes.Count, memory.DataReads);
            Assert.Equal((byte)1, memory.Read(5));
            long reads = memory.DataReads;
            memory.Writes.Clear();
            processor.Call(image.PlayAddress, 8000);
            Assert.Equal(reads, memory.DataReads);
            Assert.DoesNotContain(memory.Writes, write => write.Address is >= 0x4000 and <= 0x4017 || write.Address == 0x5FF9);
        }

        /// <summary>同値 trigger を保持し、APU への書き込み cycle は命令の最終 cycle と一致する。</summary>
        [Fact]
        public void PlayPreservesRepeatedWritesAndCycleOrder()
        {
            var timeline = new NsfFrameTimeline(0, new[]
            {
                new NsfRegisterWrite(0, 0x4003, 0), new NsfRegisterWrite(0, 0x4003, 0),
                new NsfRegisterWrite(0, 0x4015, 0)
            });
            var (data, image) = NsfExecutionFixture.Build(timeline);
            Limited6502Memory memory = NsfExecutionFixture.Map(image, data.Bytes.ToArray());
            var processor = new Limited6502(memory);
            processor.Call(image.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, memory, image.PlayAddress, timeline, image.MaximumPlayCycles);
            long[] cycles = memory.Writes.Where(write => write.Address is >= 0x4000 and <= 0x4017).Select(write => write.Cycle).ToArray();
            Assert.Equal(3, cycles.Length);
            Assert.True(cycles[0] < cycles[1] && cycles[1] < cycles[2]);
            Assert.All(cycles, cycle => Assert.InRange(cycle, 1, image.MaximumPlayCycles));
        }

        /// <summary>未知データ命令・全禁止アドレス・ゼロ WAIT は停止して RTS に戻り、再読出ししない。</summary>
        [Theory]
        [InlineData(0xFF, 0, 0)]
        [InlineData(0, 0x09, 1)]
        [InlineData(0, 0x0D, 1)]
        [InlineData(0, 0x12, 1)]
        [InlineData(0, 0x13, 1)]
        [InlineData(0, 0x14, 1)]
        [InlineData(0, 0x16, 1)]
        [InlineData(0, 0x18, 1)]
        [InlineData(0, 0xFF, 1)]
        [InlineData(1, 0, 0)]
        public void InvalidDataStopsDefensively(byte command, byte firstOperand, byte secondOperand)
        {
            var (_, image) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, new NsfRegisterWrite[0]));
            Limited6502Memory memory = NsfExecutionFixture.Map(image, new byte[] { command, firstOperand, secondOperand, 0xFF });
            var processor = new Limited6502(memory);
            processor.Call(image.InitAddress, 20000);
            memory.Writes.Clear();
            Assert.InRange(processor.Call(image.PlayAddress, 8000), 1, image.MaximumPlayCycles);
            var stop = Assert.Single(memory.Writes, write => write.Address is >= 0x4000 and <= 0x4017);
            Assert.Equal((0x4015, 0), ((int)stop.Address, (int)stop.Value));
            Assert.Equal((byte)1, memory.Read(5));
            long reads = memory.DataReads;
            memory.Writes.Clear();
            processor.Call(image.PlayAddress, 8000);
            Assert.Equal(reads, memory.DataReads);
            Assert.DoesNotContain(memory.Writes, write => write.Address is >= 0x4000 and <= 0x4017 || write.Address == 0x5FF9);
        }
    }
}
