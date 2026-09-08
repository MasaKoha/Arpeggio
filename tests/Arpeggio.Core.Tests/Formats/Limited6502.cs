using System;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>NSF 検証専用の文書化 6502 命令サブセット。命令表・サイクル解析を生成側と共有しない。</summary>
    internal sealed class Limited6502
    {
        private const byte CarryMask = 0x01;
        private const byte ZeroMask = 0x02;
        private const byte NegativeMask = 0x80;
        private const int PageMask = 0xFF00;
        private const int StackPage = 0x0100;
        private const int ByteShift = 8;
        private const ushort HostReturnAddress = 0x7FF0;
        private const int ImpliedCycles = 2;
        private const int ImmediateCycles = 2;
        private const int ZeroPageCycles = 3;
        private const int AbsoluteCycles = 4;
        private const int IndexedStoreCycles = 5;
        private const int IndirectReadCycles = 5;
        private const int ReadModifyWriteCycles = 5;
        private const int SubroutineCycles = 6;
        private const int JumpCycles = 3;
        private readonly Limited6502Memory _memory;

        internal Limited6502(Limited6502Memory memory) => _memory = memory;

        internal byte Accumulator { get; set; }
        internal byte IndexX { get; set; }
        internal byte IndexY { get; set; }
        internal byte Status { get; set; }
        internal byte StackPointer { get; set; } = byte.MaxValue;
        internal ushort ProgramCounter { get; set; }
        internal long Cycles { get; private set; }

        internal long Call(ushort address, long cycleLimit)
        {
            byte originalStackPointer = StackPointer;
            Cycles = 0;
            // 呼び出し元の JSR 時間は INIT／PLAY の予算に含めない。
            Push((byte)((HostReturnAddress - 1) >> ByteShift));
            Push((byte)((HostReturnAddress - 1) & byte.MaxValue));
            ProgramCounter = address;
            while (ProgramCounter != HostReturnAddress)
            {
                Step();
                if (Cycles > cycleLimit)
                {
                    throw new InvalidOperationException("6502 呼び出しがサイクル上限内に復帰しませんでした。");
                }
            }
            if (StackPointer != originalStackPointer)
            {
                throw new InvalidOperationException("6502 呼び出しがスタックを復元しませんでした。");
            }
            return Cycles;
        }

        internal void Step()
        {
            byte opcode = Fetch();
            // 数値は独立した 6502 ISA の符号。未実装命令を NOP として隠さない。
            switch (opcode)
            {
                case 0x05: LoadAccumulator((byte)(Accumulator | _memory.Read(Fetch())), ZeroPageCycles); break;
                case 0x18: SetFlag(CarryMask, false); Cycles += ImpliedCycles; break;
                case 0x20: JumpSubroutine(); break;
                case 0x38: SetFlag(CarryMask, true); Cycles += ImpliedCycles; break;
                case 0x4C: ProgramCounter = FetchAddress(); Cycles += JumpCycles; break;
                case 0x60: ReturnSubroutine(); break;
                case 0x85: Store(Fetch(), ZeroPageCycles); break;
                case 0x8D: Store(FetchAddress(), AbsoluteCycles); break;
                case 0x90: Branch((Status & CarryMask) == 0); break;
                case 0x9D: Store(unchecked((ushort)(FetchAddress() + IndexX)), IndexedStoreCycles); break;
                case 0xA0: IndexY = Fetch(); SetNegativeZero(IndexY); Cycles += ImmediateCycles; break;
                case 0xA5: LoadAccumulator(_memory.Read(Fetch()), ZeroPageCycles); break;
                case 0xA9: LoadAccumulator(Fetch(), ImmediateCycles); break;
                case 0xAA: IndexX = Accumulator; SetNegativeZero(IndexX); Cycles += ImpliedCycles; break;
                case 0xB1: LoadIndirectY(); break;
                case 0xBD: LoadAbsoluteX(); break;
                case 0xC6: ModifyZeroPage(-1); break;
                case 0xC9: Compare(Fetch()); break;
                case 0xD0: Branch((Status & ZeroMask) == 0); break;
                case 0xE6: ModifyZeroPage(1); break;
                case 0xF0: Branch((Status & ZeroMask) != 0); break;
                default: throw new InvalidOperationException($"未実装の 6502 opcode ${opcode:X2} を検出しました。");
            }
        }

        private byte Fetch()
        {
            byte value = _memory.Read(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            return value;
        }

        private ushort FetchAddress()
        {
            byte low = Fetch();
            return (ushort)(low | Fetch() << ByteShift);
        }

        private void LoadAccumulator(byte value, int cycles)
        {
            Accumulator = value;
            SetNegativeZero(value);
            Cycles += cycles;
        }

        private void LoadAbsoluteX()
        {
            ushort address = FetchAddress();
            ushort indexedAddress = unchecked((ushort)(address + IndexX));
            LoadAccumulator(_memory.Read(indexedAddress), AbsoluteCycles + PageCrossCycles(address, indexedAddress));
        }

        private void LoadIndirectY()
        {
            byte pointer = Fetch();
            ushort address = (ushort)(_memory.Read(pointer) | _memory.Read(unchecked((byte)(pointer + 1))) << ByteShift);
            ushort indexedAddress = unchecked((ushort)(address + IndexY));
            LoadAccumulator(_memory.Read(indexedAddress), IndirectReadCycles + PageCrossCycles(address, indexedAddress));
        }

        private void Store(ushort address, int cycles)
        {
            Cycles += cycles;
            _memory.Write(address, Accumulator, Cycles);
        }

        private void ModifyZeroPage(int delta)
        {
            byte address = Fetch();
            byte original = _memory.Read(address);
            byte value = unchecked((byte)(original + delta));
            Cycles += ReadModifyWriteCycles;
            // NMOS 6502 の RMW は最後から二番目の cycle に旧値を書き戻す。
            _memory.Write(address, original, Cycles - 1);
            _memory.Write(address, value, Cycles);
            SetNegativeZero(value);
        }

        private void Compare(byte value)
        {
            SetFlag(CarryMask, Accumulator >= value);
            SetNegativeZero(unchecked((byte)(Accumulator - value)));
            Cycles += ImmediateCycles;
        }

        private void Branch(bool taken)
        {
            sbyte displacement = unchecked((sbyte)Fetch());
            Cycles += ImmediateCycles;
            if (!taken)
            {
                return;
            }
            ushort destination = unchecked((ushort)(ProgramCounter + displacement));
            Cycles += 1 + PageCrossCycles(ProgramCounter, destination);
            ProgramCounter = destination;
        }

        private void JumpSubroutine()
        {
            ushort destination = FetchAddress();
            ushort returnAddress = unchecked((ushort)(ProgramCounter - 1));
            Cycles += SubroutineCycles;
            Push((byte)(returnAddress >> ByteShift));
            Push((byte)(returnAddress & byte.MaxValue));
            ProgramCounter = destination;
        }

        private void ReturnSubroutine()
        {
            byte low = Pop();
            ProgramCounter = unchecked((ushort)((low | Pop() << ByteShift) + 1));
            Cycles += SubroutineCycles;
        }

        private void Push(byte value)
        {
            _memory.Write((ushort)(StackPage | StackPointer), value, Cycles);
            StackPointer = unchecked((byte)(StackPointer - 1));
        }

        private byte Pop()
        {
            StackPointer = unchecked((byte)(StackPointer + 1));
            return _memory.Read((ushort)(StackPage | StackPointer));
        }

        private void SetNegativeZero(byte value)
        {
            SetFlag(ZeroMask, value == 0);
            SetFlag(NegativeMask, (value & NegativeMask) != 0);
        }

        private void SetFlag(byte mask, bool enabled)
            => Status = (byte)(enabled ? Status | mask : Status & ~mask);

        private static int PageCrossCycles(ushort first, ushort second)
            => (first & PageMask) == (second & PageMask) ? 0 : 1;
    }
}
