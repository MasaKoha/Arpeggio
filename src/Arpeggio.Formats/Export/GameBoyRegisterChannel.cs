namespace Arpeggio.Formats.Export
{
    /// <summary>一声の配置と差分書き込みに必要な直前値を保持する。</summary>
    internal sealed class GameBoyRegisterChannel
    {
        private const int UnwrittenValue = -1;

        internal GameBoyRegisterChannel(ushort lengthDutyAddress, ushort volumeAddress,
            ushort frequencyLowAddress, ushort frequencyHighAddress, byte routingMask)
        {
            LengthDutyAddress = lengthDutyAddress;
            VolumeAddress = volumeAddress;
            FrequencyLowAddress = frequencyLowAddress;
            FrequencyHighAddress = frequencyHighAddress;
            RoutingMask = routingMask;
        }

        internal ushort LengthDutyAddress { get; }
        internal ushort VolumeAddress { get; }
        internal ushort FrequencyLowAddress { get; }
        internal ushort FrequencyHighAddress { get; }
        internal byte RoutingMask { get; }
        internal int Duty { get; set; } = UnwrittenValue;
        internal int Volume { get; set; } = UnwrittenValue;
        internal int FrequencyLow { get; set; } = UnwrittenValue;
        internal int FrequencyHigh { get; set; } = UnwrittenValue;
    }
}
