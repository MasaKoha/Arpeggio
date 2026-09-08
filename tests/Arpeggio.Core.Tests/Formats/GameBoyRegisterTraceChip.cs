using System;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>DMG 生成サブセットの周期・DAC・再 trigger・波形 RAM・routing を独立再構成する。</summary>
    internal sealed class GameBoyRegisterTraceChip : IRegisterTraceChip
    {
        private const int SweepAddress = 0xFF10;
        private const int PulseOneDutyAddress = 0xFF11;
        private const int PulseOneEnvelopeAddress = 0xFF12;
        private const int PulseOneLowAddress = 0xFF13;
        private const int PulseOneHighAddress = 0xFF14;
        private const int PulseTwoDutyAddress = 0xFF16;
        private const int PulseTwoEnvelopeAddress = 0xFF17;
        private const int PulseTwoLowAddress = 0xFF18;
        private const int PulseTwoHighAddress = 0xFF19;
        private const int WaveDacAddress = 0xFF1A;
        private const int WaveVolumeAddress = 0xFF1C;
        private const int WaveLowAddress = 0xFF1D;
        private const int WaveHighAddress = 0xFF1E;
        private const int NoiseEnvelopeAddress = 0xFF21;
        private const int NoiseFrequencyAddress = 0xFF22;
        private const int NoiseTriggerAddress = 0xFF23;
        private const int MasterVolumeAddress = 0xFF24;
        private const int RoutingAddress = 0xFF25;
        private const int PowerAddress = 0xFF26;
        private const int VinMask = 0x88;
        private const int LengthMask = 0x3F;
        private const int WaveVolumeMask = 0x60;
        private const int ChannelCount = 4;
        private const int WaveChannel = 2;
        private const int ChannelRegisterStride = 5;
        private const int NoiseChannel = 3;
        private const int WaveRamStart = 0xFF30;
        private const int WaveRamEnd = 0xFF3F;
        private const int WaveSamples = 32;
        private const int PulseSteps = 8;
        private const int MasterVolumeLevels = 8;
        private const int PulseClockDivider = 4;
        private const int WaveClockDivider = 2;
        private const int MaximumNoiseShift = 13;
        private const int SamplesPerWaveByte = 2;
        private const int FrequencyLimit = 2048;
        private const int HighMask = 7;
        private const int NibbleBits = 4;
        private const int ByteBits = 8;
        private const int DutyShift = 6;
        private const int VolumeShift = 5;
        private const int NibbleMask = 15;
        private const int TriggerBit = 0x80;
        private const int NoiseWidthBit = 8;
        private const int NoiseFeedbackBit = 14;
        private const int NoiseShortFeedbackBit = 6;
        private static readonly int[] DutyPatterns = { 0x80, 0x81, 0xE1, 0x7E };
        private static readonly int[] NoiseDivisors = { 8, 16, 32, 48, 64, 80, 96, 112 };
        private readonly int[] _frequencies = new int[ChannelCount];
        private readonly int[] _positions = new int[ChannelCount];
        private readonly int[] _remaining = new int[ChannelCount];
        private readonly int[] _duties = new int[ChannelCount];
        private readonly int[] _envelopes = new int[ChannelCount];
        private readonly int[] _volumes = new int[ChannelCount];
        private readonly int[] _triggers = new int[ChannelCount];
        private readonly bool[] _dacEnabled = new bool[ChannelCount];
        private readonly bool[] _active = new bool[ChannelCount];
        private readonly bool[] _pulseClocked = new bool[WaveChannel];
        private readonly int[] _wave = new int[WaveSamples];
        private bool _powered;
        private int _routing;
        private int _masterVolume;
        private int _waveVolume;
        private int _noiseRegister;

        /// <summary>DMG の基本クロック。</summary>
        public int ClockRate => 4194304;
        /// <summary>NR51 と NR50 を適用した検証用の左右デジタル和。</summary>
        public (double Left, double Right) Output
        {
            get
            {
                double left = 0;
                double right = 0;
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    int level = Level(channel);
                    if ((_routing & (1 << channel)) != 0)
                    {
                        right += level;
                    }
                    if ((_routing & (1 << (channel + NibbleBits))) != 0)
                    {
                        left += level;
                    }
                }
                return (left * (((_masterVolume >> NibbleBits) & HighMask) + 1) / MasterVolumeLevels,
                    right * ((_masterVolume & HighMask) + 1) / MasterVolumeLevels);
            }
        }

        internal int NoiseState { get; private set; }
        internal int NoisePeriod => NoiseDivisors[_noiseRegister & HighMask] << (_noiseRegister >> NibbleBits);
        internal int Routing => _routing;
        internal int Frequency(int channel) => _frequencies[channel];
        internal int Position(int channel) => _positions[channel];
        internal int RemainingCycles(int channel) => _remaining[channel];
        internal int Triggers(int channel) => _triggers[channel];
        internal bool IsActive(int channel) => _active[channel];
        internal bool DacEnabled(int channel) => _dacEnabled[channel];
        internal int WaveSample(int position) => _wave[position];

        internal int Level(int channel)
        {
            if (!_powered || !_active[channel] || !_dacEnabled[channel])
            {
                return 0;
            }
            if (channel == WaveChannel)
            {
                return _waveVolume == 0 ? 0 : _wave[_positions[channel]] >> (_waveVolume - 1);
            }
            if (channel == NoiseChannel)
            {
                // Pan Docs の XNOR／zero seed 表現を採り、出力 bit をそのまま読む。
                return (NoiseState & 1) != 0 ? _volumes[channel] : 0;
            }
            if (!_pulseClocked[channel])
            {
                return 0;
            }
            return ((DutyPatterns[_duties[channel]] >> _positions[channel]) & 1) != 0 ? _volumes[channel] : 0;
        }

        /// <summary>生成サブセットだけを受け、DAC off 中の RAM 書き込みと trigger 副作用を検証する。</summary>
        public void Apply(int address, int value)
        {
            Require(value is >= 0 and <= byte.MaxValue);
            if (address == PowerAddress)
            {
                Require(value is 0 or TriggerBit);
                _powered = value != 0;
                if (!_powered)
                {
                    ResetPower();
                }
                return;
            }
            Require(_powered);
            if (address is >= WaveRamStart and <= WaveRamEnd)
            {
                Require(!_dacEnabled[WaveChannel]);
                int position = (address - WaveRamStart) * SamplesPerWaveByte;
                _wave[position] = value >> NibbleBits;
                _wave[position + 1] = value & NibbleMask;
                return;
            }
            ApplyRegister(address, value);
        }

        private void ApplyRegister(int address, int value)
        {
            switch (address)
            {
                case SweepAddress:
                    Require(value == 0);
                    break;
                case MasterVolumeAddress:
                    Require((value & VinMask) == 0);
                    _masterVolume = value;
                    break;
                case RoutingAddress:
                    _routing = value;
                    break;
                case PulseOneDutyAddress: case PulseTwoDutyAddress:
                    Require((value & LengthMask) == 0);
                    _duties[address == PulseOneDutyAddress ? 0 : 1] = value >> DutyShift;
                    break;
                case PulseOneEnvelopeAddress: case PulseTwoEnvelopeAddress: case NoiseEnvelopeAddress:
                    SetEnvelope((address - PulseOneEnvelopeAddress) / ChannelRegisterStride, value);
                    break;
                case PulseOneLowAddress: case PulseTwoLowAddress: case WaveLowAddress:
                    SetFrequencyLow((address - PulseOneLowAddress) / ChannelRegisterStride, value);
                    break;
                case PulseOneHighAddress: case PulseTwoHighAddress: case WaveHighAddress:
                    SetFrequencyHigh((address - PulseOneHighAddress) / ChannelRegisterStride, value);
                    break;
                case WaveDacAddress:
                    Require(value is 0 or TriggerBit);
                    SetDac(WaveChannel, value != 0);
                    break;
                case WaveVolumeAddress:
                    Require((value & ~WaveVolumeMask) == 0);
                    _waveVolume = value >> VolumeShift;
                    break;
                case NoiseFrequencyAddress:
                    Require((value >> NibbleBits) <= MaximumNoiseShift);
                    _noiseRegister = value;
                    break;
                case NoiseTriggerAddress:
                    Require(value == TriggerBit);
                    Trigger(NoiseChannel);
                    break;
                default:
                    throw new InvalidOperationException("未対応の GB レジスタです。");
            }
        }

        private void ResetPower()
        {
            Array.Clear(_frequencies);
            Array.Clear(_positions);
            Array.Clear(_remaining);
            Array.Clear(_duties);
            Array.Clear(_envelopes);
            Array.Clear(_volumes);
            Array.Clear(_dacEnabled);
            Array.Clear(_active);
            Array.Clear(_pulseClocked);
            _routing = 0;
            _masterVolume = 0;
            _waveVolume = 0;
            _noiseRegister = 0;
            NoiseState = 0;
        }

        private void SetEnvelope(int channel, int value)
        {
            Require((value & NibbleMask) == 0);
            Require(value == 0 || !_active[channel]);
            _envelopes[channel] = value >> NibbleBits;
            SetDac(channel, value != 0);
        }

        private void SetDac(int channel, bool enabled)
        {
            _dacEnabled[channel] = enabled;
            if (!enabled)
            {
                _active[channel] = false;
            }
        }

        private void SetFrequencyLow(int channel, int value)
            => _frequencies[channel] = (_frequencies[channel] & (HighMask << ByteBits)) | value;

        private void SetFrequencyHigh(int channel, int value)
        {
            Require((value & ~(TriggerBit | HighMask)) == 0);
            _frequencies[channel] = (_frequencies[channel] & byte.MaxValue) | ((value & HighMask) << ByteBits);
            if ((value & TriggerBit) != 0)
            {
                Trigger(channel);
            }
        }

        private void Trigger(int channel)
        {
            _triggers[channel]++;
            _active[channel] = _dacEnabled[channel];
            _volumes[channel] = _envelopes[channel];
            _remaining[channel] = Period(channel);
            if (channel == WaveChannel)
            {
                _positions[channel] = 0;
            }
            if (channel == NoiseChannel)
            {
                NoiseState = 0;
            }
        }

        /// <summary>一定音量と length 無効の条件で、整数クロックから各 sequencer を進める。</summary>
        public void AdvanceCycles(int cycles)
        {
            Require(cycles >= 0);
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                if (!_active[channel])
                {
                    continue;
                }
                AdvanceChannel(channel, cycles);
            }
        }

        private void AdvanceChannel(int channel, int cycles)
        {
            while (cycles >= _remaining[channel])
            {
                cycles -= _remaining[channel];
                if (channel == NoiseChannel)
                {
                    ClockNoise();
                }
                else
                {
                    int steps = channel == WaveChannel ? WaveSamples : PulseSteps;
                    _positions[channel] = (_positions[channel] + 1) % steps;
                    if (channel < WaveChannel)
                    {
                        _pulseClocked[channel] = true;
                    }
                }
                _remaining[channel] = Period(channel);
            }
            _remaining[channel] -= cycles;
        }

        private int Period(int channel)
        {
            if (channel == NoiseChannel)
            {
                return NoisePeriod;
            }
            return (FrequencyLimit - _frequencies[channel]) * (channel == WaveChannel ? WaveClockDivider : PulseClockDivider);
        }

        private void ClockNoise()
        {
            int feedback = 1 ^ ((NoiseState ^ (NoiseState >> 1)) & 1);
            NoiseState = (NoiseState >> 1) | (feedback << NoiseFeedbackBit);
            if ((_noiseRegister & NoiseWidthBit) != 0)
            {
                NoiseState = (NoiseState & ~(1 << NoiseShortFeedbackBit)) | (feedback << NoiseShortFeedbackBit);
            }
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("GB 再合成器の検証サブセット外です。");
            }
        }
    }
}
