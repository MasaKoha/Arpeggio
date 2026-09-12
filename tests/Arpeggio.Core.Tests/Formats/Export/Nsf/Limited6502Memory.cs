using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>限定 CPU の RAM・読み取り専用 bank・書き込み観測を提供する。生成側の型には依存しない。</summary>
    internal sealed class Limited6502Memory
    {
        private const int AddressSpaceBytes = 0x10000;
        private const int RomStart = 0x8000;
        private const int BankBytes = 0x1000;
        private const int BankRegisterStart = 0x5FF8;
        private const int BankRegisterEnd = 0x5FFF;
        private const int BankSlotCount = 8;
        private const int DataBankSlot = 1;
        private readonly byte[] _memory = new byte[AddressSpaceBytes];
        private readonly byte[] _banks = new byte[BankSlotCount];
        private byte[]? _rom;

        internal List<(long Frame, long Cycle, ushort Address, byte Value)> Writes { get; } = new();
        internal long Frame { get; set; } = -1;
        internal long DataReads { get; private set; }

        internal void Load(int address, params byte[] bytes) => bytes.CopyTo(_memory, address);

        internal void MapRom(byte[] rom, byte[] banks)
        {
            if (rom.Length == 0 || rom.Length % BankBytes != 0 || banks.Length != _banks.Length)
            {
                throw new ArgumentException("ROM は 4 KiB 整列、初期 bank は 8 個を指定してください。");
            }
            _rom = (byte[])rom.Clone();
            banks.CopyTo(_banks, 0);
        }

        internal byte Read(ushort address)
        {
            if (_rom is null || address < RomStart)
            {
                return _memory[address];
            }
            int slot = (address - RomStart) / BankBytes;
            int offset = _banks[slot] * BankBytes + address % BankBytes;
            if (offset >= _rom.Length)
            {
                throw new InvalidOperationException("実在しない ROM bank を読みました。");
            }
            if (slot == DataBankSlot)
            {
                DataReads++;
            }
            return _rom[offset];
        }

        internal void Write(ushort address, byte value, long cycle)
        {
            if (_rom is not null && address >= RomStart)
            {
                throw new InvalidOperationException("ROM への自己書き換えを検出しました。");
            }
            if (_rom is not null && !(address <= 0x1F || address is >= 0x100 and <= 0x1FF ||
                address is >= 0x4000 and <= 0x4017 || address == 0x5FF9))
            {
                throw new InvalidOperationException("NSF の予約領域外への書き込みを検出しました。");
            }
            if (address is >= BankRegisterStart and <= BankRegisterEnd)
            {
                _banks[address - BankRegisterStart] = value;
            }
            _memory[address] = value;
            // 長曲でも観測量を APU／bank 書き込み数に抑える。生の CPU 単体テストではスタックと RMW も記録する。
            if (_rom is null || address >= 0x4000)
            {
                Writes.Add((Frame, cycle, address, value));
            }
        }
    }
}
