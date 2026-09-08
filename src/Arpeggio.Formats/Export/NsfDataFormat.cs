namespace Arpeggio.Formats.Export
{
    /// <summary>NSF データ列と固定 bank の共通配置。</summary>
    internal static class NsfDataFormat
    {
        internal const byte Write = 0x00;
        internal const byte Wait = 0x01;
        internal const byte End = 0x02;
        internal const int OperandCommandBytes = 3;
        internal const int EndBytes = 1;
        internal const int MaximumWait = ushort.MaxValue;
        internal const int ByteShift = 8;
        internal const int ByteMask = byte.MaxValue;
        internal const ushort RegisterBase = 0x4000;
        internal const int RegisterOffsetCount = 0x18;
        internal const ushort CodeAddress = 0x8000;
        internal const ushort DataAddress = 0x9000;
        internal const ushort DataEndAddress = 0xA000;
        internal const ushort BankRegister = 0x5FF9;
        internal const byte FirstDataBank = 1;
        internal const byte LastDataBank = byte.MaxValue;

        internal static bool IsAllowedAddress(ushort address)
            => address is NesRegisters.PulseOneControl or NesRegisters.PulseOneSweep or
                NesRegisters.PulseOneTimerLow or NesRegisters.PulseOneTimerHigh or
                NesRegisters.PulseTwoControl or NesRegisters.PulseTwoSweep or
                NesRegisters.PulseTwoTimerLow or NesRegisters.PulseTwoTimerHigh or
                NesRegisters.TriangleLinear or NesRegisters.TriangleTimerLow or NesRegisters.TriangleTimerHigh or
                NesRegisters.NoiseControl or NesRegisters.NoisePeriod or NesRegisters.NoiseLength or
                NesRegisters.DmcControl or NesRegisters.DmcOutput or NesRegisters.Status or NesRegisters.FrameCounter;
    }
}
