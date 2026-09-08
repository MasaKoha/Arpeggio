using System;
using System.Linq;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>生成コードを使わず、手書き命令列で全使用 opcode の状態・アドレス・サイクルを固定する。</summary>
    public sealed class Limited6502Tests
    {
        private const ushort ProgramAddress = 0x2000;

        /// <summary>全ロード・論理和・転送命令は N/Z だけを更新し、対応レジスタへ値を渡す。</summary>
        [Theory]
        [InlineData(0x05, 3)]
        [InlineData(0xA0, 2)]
        [InlineData(0xA5, 3)]
        [InlineData(0xA9, 2)]
        [InlineData(0xAA, 2)]
        [InlineData(0xB1, 5)]
        [InlineData(0xBD, 4)]
        public void LoadsTransferAndOrSetOnlyNegativeAndZero(byte opcode, long expectedCycles)
        {
            foreach (byte value in new byte[] { 0, 0x7F, 0x80, 0xFF })
            {
                foreach (byte initialStatus in new byte[] { 0, 0xFF })
                {
                    var memory = new Limited6502Memory();
                    byte[] program = opcode switch
                    {
                        0x05 or 0xA5 => new byte[] { opcode, 0x40 },
                        0xB1 => new byte[] { opcode, 0x50 },
                        0xBD => new byte[] { opcode, 0x30, 0x30 },
                        _ => new byte[] { opcode, value }
                    };
                    memory.Load(ProgramAddress, program);
                    memory.Load(0x40, value);
                    memory.Load(0x50, 0x40, 0x00);
                    memory.Load(0x3030, value);
                    var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, Status = initialStatus };
                    processor.Accumulator = opcode == 0xAA ? value : (byte)0;
                    processor.Step();
                    byte actual = opcode switch { 0xAA => processor.IndexX, 0xA0 => processor.IndexY, _ => processor.Accumulator };
                    Assert.Equal(value, actual);
                    Assert.Equal((byte)((initialStatus & 0x7D) | (value & 0x80) | (value == 0 ? 2 : 0)), processor.Status);
                    Assert.Equal(expectedCycles, processor.Cycles);
                    int length = opcode == 0xAA ? 1 : program.Length;
                    Assert.Equal(ProgramAddress + length, processor.ProgramCounter);
                }
            }
        }

        /// <summary>ORA は元の A のビットを保持してゼロページ値と合成する。</summary>
        [Fact]
        public void OrCombinesBothOperands()
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, 0x05, 0x40);
            memory.Load(0x40, 0x81);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, Accumulator = 0x42 };
            processor.Step();
            Assert.Equal(0xC3, processor.Accumulator);
            Assert.Equal(0x80, processor.Status);
        }

        /// <summary>絶対 X と間接 Y はページ越え時だけ追加 cycle を持ち、アドレスを 16 bit で折り返す。</summary>
        [Theory]
        [InlineData(0xBD, 0x3040, 2, 0x3042, 4)]
        [InlineData(0xBD, 0x30FF, 1, 0x3100, 5)]
        [InlineData(0xBD, 0xFFFF, 1, 0x0000, 5)]
        [InlineData(0xB1, 0x3040, 2, 0x3042, 5)]
        [InlineData(0xB1, 0x30FF, 1, 0x3100, 6)]
        [InlineData(0xB1, 0xFFFF, 1, 0x0000, 6)]
        public void IndexedLoadsWrapAndChargePageCrossing(byte opcode, ushort address, byte index, ushort target, long cycles)
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, opcode, (byte)address, (byte)(address >> 8));
            if (opcode == 0xB1)
            {
                memory.Load(ProgramAddress, opcode, 0x40);
                memory.Load(0x40, (byte)address, (byte)(address >> 8));
            }
            memory.Load(target, 0x81);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, IndexX = index, IndexY = index };
            processor.Step();
            Assert.Equal(0x81, processor.Accumulator);
            Assert.Equal(0x80, processor.Status);
            Assert.Equal(cycles, processor.Cycles);
        }

        /// <summary>間接 Y のポインター上位 byte は $FF から $00 へ折り返す。</summary>
        [Fact]
        public void IndirectPointerWrapsWithinZeroPage()
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, 0xB1, 0xFF);
            memory.Load(0xFF, 0xFF, 0x99);
            memory.Load(0, 0x30);
            memory.Load(0x3100, 0x42);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, IndexY = 1 };
            processor.Step();
            Assert.Equal(0x42, processor.Accumulator);
            Assert.Equal(6L, processor.Cycles);
        }

        /// <summary>全 STA は flags を保ち、絶対 X のページ越えでも常に 5 cycles で書く。</summary>
        [Theory]
        [InlineData(0x85, 0x40, 0, 0x40, 3)]
        [InlineData(0x8D, 0x3040, 0, 0x3040, 4)]
        [InlineData(0x9D, 0x3040, 2, 0x3042, 5)]
        [InlineData(0x9D, 0x30FF, 1, 0x3100, 5)]
        [InlineData(0x9D, 0xFFFF, 1, 0, 5)]
        public void StoresPreserveFlagsAndRecordFinalCycle(byte opcode, ushort address, byte index, ushort target, long cycles)
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, opcode, (byte)address, (byte)(address >> 8));
            var processor = new Limited6502(memory)
            {
                ProgramCounter = ProgramAddress, IndexX = index, Accumulator = 0x80, Status = 0x7F
            };
            processor.Step();
            Assert.Equal((byte)0x80, memory.Read(target));
            Assert.Equal(0x7F, processor.Status);
            Assert.Equal(cycles, processor.Cycles);
            Assert.Equal((-1L, cycles, target, (byte)0x80), Assert.Single(memory.Writes));
        }

        /// <summary>INC／DEC は byte で折り返し、C/V を変えず旧値と新値を二回書く。</summary>
        [Theory]
        [InlineData(0xE6, 0xFF, 0, 0x7F)]
        [InlineData(0xE6, 0x7F, 0x80, 0xFD)]
        [InlineData(0xE6, 0, 1, 0x7D)]
        [InlineData(0xC6, 0, 0xFF, 0xFD)]
        [InlineData(0xC6, 0x80, 0x7F, 0x7D)]
        [InlineData(0xC6, 1, 0, 0x7F)]
        public void IncrementDecrementWrapAndPreserveUnrelatedFlags(byte opcode, byte original, byte expected, byte status)
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, opcode, 0x40);
            memory.Load(0x40, original);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, Status = 0xFF, Accumulator = 0x42 };
            processor.Step();
            Assert.Equal(expected, memory.Read(0x40));
            Assert.Equal(status, processor.Status);
            Assert.Equal(0x42, processor.Accumulator);
            Assert.Equal(5L, processor.Cycles);
            Assert.Equal(new[] { (4L, original), (5L, expected) }, memory.Writes.Select(write => (write.Cycle, write.Value)));
        }

        /// <summary>CMP は符号なし C と減算結果の N/Z を作り、A と V を保持する。</summary>
        [Theory]
        [InlineData(0, 0, 0x7F)]
        [InlineData(0, 1, 0xFC)]
        [InlineData(0x80, 1, 0x7D)]
        [InlineData(0xFF, 0, 0xFD)]
        [InlineData(0x7F, 0xFF, 0xFC)]
        public void CompareUsesUnsignedCarryAndWrappedSign(byte accumulator, byte operand, byte status)
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, 0xC9, operand);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, Accumulator = accumulator, Status = 0xFF };
            processor.Step();
            Assert.Equal(accumulator, processor.Accumulator);
            Assert.Equal(status, processor.Status);
            Assert.Equal(2L, processor.Cycles);
        }

        /// <summary>CLC／SEC は C のみを変更する。</summary>
        [Theory]
        [InlineData(0x18, 0xFF, 0xFE)]
        [InlineData(0x18, 0, 0)]
        [InlineData(0x38, 0, 1)]
        [InlineData(0x38, 0xFF, 0xFF)]
        public void CarryInstructionsPreserveOtherFlags(byte opcode, byte initial, byte expected)
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, opcode);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress, Status = initial };
            processor.Step();
            Assert.Equal(expected, processor.Status);
            Assert.Equal(2L, processor.Cycles);
        }

        /// <summary>三種の分岐は不成立・成立・前後ページ越えで 2／3／4 cycles となり flags を保持する。</summary>
        [Theory]
        [InlineData(0x90, 0xFE, 0xFF)]
        [InlineData(0xD0, 0xFD, 0xFF)]
        [InlineData(0xF0, 0xFF, 0xFD)]
        public void BranchesUseSignedDisplacementAndPostOperandPage(byte opcode, byte takenStatus, byte skippedStatus)
        {
            foreach (var example in new[]
            {
                (Start: 0x2040, Offset: 5, Target: 0x2047, Cycles: 3L),
                (Start: 0x2040, Offset: 0xFC, Target: 0x203E, Cycles: 3L),
                (Start: 0x20FD, Offset: 1, Target: 0x2100, Cycles: 4L),
                (Start: 0x20FE, Offset: 0xFE, Target: 0x20FE, Cycles: 4L),
                (Start: 0xFFFD, Offset: 1, Target: 0, Cycles: 4L)
            })
            {
                foreach (bool taken in new[] { false, true })
                {
                    var memory = new Limited6502Memory();
                    memory.Load(example.Start, opcode, (byte)example.Offset);
                    byte status = taken ? takenStatus : skippedStatus;
                    var processor = new Limited6502(memory) { ProgramCounter = (ushort)example.Start, Status = status };
                    processor.Step();
                    Assert.Equal(taken ? example.Target : example.Start + 2, processor.ProgramCounter);
                    Assert.Equal(taken ? example.Cycles : 2L, processor.Cycles);
                    Assert.Equal(status, processor.Status);
                }
            }
        }

        /// <summary>JMP は little-endian の絶対アドレスへ移り、命令 fetch は $FFFF から折り返す。</summary>
        [Fact]
        public void JumpAndInstructionFetchWrapAtAddressSpaceEnd()
        {
            var memory = new Limited6502Memory();
            memory.Load(0xFFFF, 0x4C);
            memory.Load(0, 0x34, 0x12);
            var processor = new Limited6502(memory) { ProgramCounter = 0xFFFF, Status = 0xFF };
            processor.Step();
            Assert.Equal(0x1234, processor.ProgramCounter);
            Assert.Equal(0xFF, processor.Status);
            Assert.Equal(3L, processor.Cycles);
        }

        /// <summary>入れ子 JSR／RTS は戻り位置の上位→下位を保存し、SP の折り返しを含め対称に復元する。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(0xFF)]
        public void NestedSubroutinesPreserveStackAndFlags(byte initialStackPointer)
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, 0x20, 0x00, 0x30);
            memory.Load(0x3000, 0x20, 0x00, 0x40, 0x60);
            memory.Load(0x4000, 0x60);
            var processor = new Limited6502(memory)
            {
                ProgramCounter = ProgramAddress, StackPointer = initialStackPointer, Status = 0xFF
            };
            processor.Step();
            Assert.Equal(0x3000, processor.ProgramCounter);
            Assert.Equal((byte)0x20, memory.Read((ushort)(0x100 | initialStackPointer)));
            Assert.Equal((byte)0x02, memory.Read((ushort)(0x100 | unchecked((byte)(initialStackPointer - 1)))));
            processor.Step();
            Assert.Equal(0x4000, processor.ProgramCounter);
            processor.Step();
            Assert.Equal(0x3003, processor.ProgramCounter);
            processor.Step();
            Assert.Equal(0x2003, processor.ProgramCounter);
            Assert.Equal(initialStackPointer, processor.StackPointer);
            Assert.Equal(0xFF, processor.Status);
            Assert.Equal(24L, processor.Cycles);
        }

        /// <summary>未実装 opcode と非復帰コードは明示的に失敗し、RTS の境界予算は受理する。</summary>
        [Fact]
        public void UnknownOpcodesAndInfiniteLoopsFailInsteadOfSilentlyStopping()
        {
            var memory = new Limited6502Memory();
            memory.Load(ProgramAddress, 0x00);
            var processor = new Limited6502(memory) { ProgramCounter = ProgramAddress };
            Assert.Throws<InvalidOperationException>(() => processor.Step());
            memory.Load(ProgramAddress, 0x4C, 0x00, 0x20);
            Assert.Throws<InvalidOperationException>(() => processor.Call(ProgramAddress, 20));
            memory.Load(ProgramAddress, 0x60);
            Assert.Equal(6L, processor.Call(ProgramAddress, 6));
            Assert.Throws<InvalidOperationException>(() => processor.Call(ProgramAddress, 5));
        }
    }
}
