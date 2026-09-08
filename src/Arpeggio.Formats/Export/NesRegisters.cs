namespace Arpeggio.Formats.Export
{
    /// <summary>NES の変換で使用するレジスタアドレスとビット配置。</summary>
    internal static class NesRegisters
    {
        internal const ushort PulseOneControl = 0x4000;
        internal const ushort PulseOneSweep = 0x4001;
        internal const ushort PulseOneTimerLow = 0x4002;
        internal const ushort PulseOneTimerHigh = 0x4003;
        internal const ushort PulseTwoControl = 0x4004;
        internal const ushort PulseTwoSweep = 0x4005;
        internal const ushort PulseTwoTimerLow = 0x4006;
        internal const ushort PulseTwoTimerHigh = 0x4007;
        internal const ushort TriangleLinear = 0x4008;
        internal const ushort TriangleTimerLow = 0x400A;
        internal const ushort TriangleTimerHigh = 0x400B;
        internal const ushort NoiseControl = 0x400C;
        internal const ushort NoisePeriod = 0x400E;
        internal const ushort NoiseLength = 0x400F;
        internal const ushort DmcControl = 0x4010;
        internal const ushort DmcOutput = 0x4011;
        internal const ushort Status = 0x4015;
        internal const ushort FrameCounter = 0x4017;
        internal const byte SweepDisabled = 0x08;
        internal const byte FiveStepInterruptDisabled = 0xC0;
        internal const byte TriangleLinearHeld = 0xFF;
        internal const byte LengthHaltConstantVolume = 0x30;
        internal const byte PulseOneEnable = 0x01;
        internal const byte PulseTwoEnable = 0x02;
        internal const byte TriangleEnable = 0x04;
        internal const byte NoiseEnable = 0x08;
        internal const int NoiseShortMode = 0x80;
        internal const int NoisePeriodMask = 0x0F;
        internal const int VolumeMask = 0x0F;
        internal const int DutyShift = 6;
        internal const int TimerHighShift = 8;
        internal const int TimerHighMask = 0x07;
        internal const int TimerLowMask = 0xFF;
        // index 0 は実 length 10。halt と併用し、On のロード後に自然減衰させない。
        internal const int LengthIndexZero = 0;
    }
}
