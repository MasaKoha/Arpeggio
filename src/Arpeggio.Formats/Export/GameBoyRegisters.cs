namespace Arpeggio.Formats.Export
{
    /// <summary>Game Boy の変換で使用するレジスタアドレスとビット配置。</summary>
    internal static class GameBoyRegisters
    {
        internal const ushort PulseOneSweep = 0xFF10;
        internal const ushort PulseOneDuty = 0xFF11;
        internal const ushort PulseOneEnvelope = 0xFF12;
        internal const ushort PulseOneFrequencyLow = 0xFF13;
        internal const ushort PulseOneFrequencyHigh = 0xFF14;
        internal const ushort PulseTwoDuty = 0xFF16;
        internal const ushort PulseTwoEnvelope = 0xFF17;
        internal const ushort PulseTwoFrequencyLow = 0xFF18;
        internal const ushort PulseTwoFrequencyHigh = 0xFF19;
        internal const ushort WaveDac = 0xFF1A;
        internal const ushort WaveLength = 0xFF1B;
        internal const ushort WaveVolume = 0xFF1C;
        internal const ushort WaveFrequencyLow = 0xFF1D;
        internal const ushort WaveFrequencyHigh = 0xFF1E;
        internal const ushort NoiseLength = 0xFF20;
        internal const ushort NoiseEnvelope = 0xFF21;
        internal const ushort NoiseFrequency = 0xFF22;
        internal const ushort NoiseTrigger = 0xFF23;
        internal const ushort MasterVolume = 0xFF24;
        internal const ushort Routing = 0xFF25;
        internal const ushort Power = 0xFF26;
        internal const ushort WaveRamStart = 0xFF30;
        internal const byte PowerEnabled = 0x80;
        internal const byte MasterVolumeMaximum = 0x77;
        internal const byte DacEnabled = 0x80;
        internal const byte Trigger = 0x80;
        internal const byte PulseOneRouting = 0x11;
        internal const byte PulseTwoRouting = 0x22;
        internal const byte WaveRouting = 0x44;
        internal const byte NoiseRouting = 0x88;
        internal const int NoiseWidthFlag = 0x08;
        internal const int NoiseClockShift = 4;
        internal const byte LeftRoutingMask = 0xF0;
        internal const byte RightRoutingMask = 0x0F;
        internal const int DutyShift = 6;
        internal const int EnvelopeVolumeShift = 4;
        internal const int FrequencyHighShift = 8;
        internal const int FrequencyHighMask = 0x07;
        internal const int FrequencyLowMask = 0xFF;
        internal const int WaveRamBytes = 16;
        internal const int SamplesPerWaveByte = 2;
        internal const int WaveSampleShift = 4;
    }
}
