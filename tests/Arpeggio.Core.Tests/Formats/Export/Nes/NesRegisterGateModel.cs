using System;
using Arpeggio.Formats.Export;

namespace Arpeggio.Core.Tests.Formats.Export.Nes
{
    /// <summary>PCM を作らず、生成命令の enable・length load・linear reload・Noise mode を独立して読む。</summary>
    internal sealed class NesRegisterGateModel
    {
        private const int ChannelCount = 4;
        private const int TriangleChannel = 2;
        private const int LengthCounterValue = 10;
        private const int LengthIndexMask = 0xF8;
        private const int LinearReloadMask = 0x7F;
        private const int FrameCounterImmediateClock = 0x80;
        private const int NoiseShortBit = 0x80;
        private const int NoisePeriodMask = 0x0F;
        private const int VolumeMask = 0x0F;
        private readonly int[] _lengths = new int[ChannelCount];
        private readonly int[] _loads = new int[ChannelCount];
        private readonly int[] _volumes = new int[ChannelCount];
        private int _linearReloadValue;
        private bool _linearReloadPending;

        internal int EnabledChannels { get; private set; }
        internal int TriangleLinear { get; private set; }
        internal bool NoiseShort { get; private set; }
        internal int NoisePeriod { get; private set; }
        internal int PulsePhaseRestarts { get; private set; }

        internal int Length(int channel) => _lengths[channel];
        internal int Loads(int channel) => _loads[channel];
        internal int Volume(int channel) => _volumes[channel];
        internal bool IsGated(int channel) => _lengths[channel] > 0 && (channel != TriangleChannel || TriangleLinear > 0);

        internal void Apply(RegisterWrite write)
        {
            switch (write.Address)
            {
                case 0x4015:
                    SetEnabled(write.Value);
                    break;
                case 0x4000:
                    _volumes[0] = write.Value & VolumeMask;
                    break;
                case 0x4004:
                    _volumes[1] = write.Value & VolumeMask;
                    break;
                case 0x400C:
                    _volumes[3] = write.Value & VolumeMask;
                    break;
                case 0x4003:
                    LoadLength(0, write.Value);
                    PulsePhaseRestarts++;
                    break;
                case 0x4007:
                    LoadLength(1, write.Value);
                    PulsePhaseRestarts++;
                    break;
                case 0x4008:
                    _linearReloadValue = write.Value & LinearReloadMask;
                    break;
                case 0x400B:
                    LoadLength(TriangleChannel, write.Value);
                    _linearReloadPending = true;
                    break;
                case 0x400E:
                    NoiseShort = (write.Value & NoiseShortBit) != 0;
                    NoisePeriod = write.Value & NoisePeriodMask;
                    break;
                case 0x400F:
                    LoadLength(3, write.Value);
                    break;
                case 0x4017:
                    if ((write.Value & FrameCounterImmediateClock) != 0 && _linearReloadPending)
                    {
                        TriangleLinear = _linearReloadValue;
                    }
                    break;
            }
        }

        private void SetEnabled(int value)
        {
            EnabledChannels = value;
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                if ((value & (1 << channel)) == 0)
                {
                    _lengths[channel] = 0;
                }
            }
        }

        private void LoadLength(int channel, int value)
        {
            if ((value & LengthIndexMask) != 0)
            {
                throw new InvalidOperationException("この検証モデルは生成仕様の length index 0 のみを扱います。");
            }
            if ((EnabledChannels & (1 << channel)) != 0)
            {
                _lengths[channel] = LengthCounterValue;
                _loads[channel]++;
            }
        }
    }
}
