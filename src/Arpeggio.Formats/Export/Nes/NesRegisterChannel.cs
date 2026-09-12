namespace Arpeggio.Formats.Export.Nes
{
    /// <summary>一声のレジスタ配置と直前に書いた値を保持する。</summary>
    internal sealed class NesRegisterChannel
    {
        private const int UnwrittenValue = -1;

        internal NesRegisterChannel(ushort controlAddress, ushort timerLowAddress, ushort timerHighAddress, byte enableMask)
        {
            ControlAddress = controlAddress;
            TimerLowAddress = timerLowAddress;
            TimerHighAddress = timerHighAddress;
            EnableMask = enableMask;
        }

        internal ushort ControlAddress { get; }
        internal ushort TimerLowAddress { get; }
        internal ushort TimerHighAddress { get; }
        internal byte EnableMask { get; }
        internal int Control { get; set; } = UnwrittenValue;
        internal int TimerLow { get; set; } = UnwrittenValue;
        internal int TimerHigh { get; set; } = UnwrittenValue;
    }
}
