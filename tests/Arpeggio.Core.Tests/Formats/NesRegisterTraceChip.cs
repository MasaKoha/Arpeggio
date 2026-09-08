using System;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>NES 生成サブセットの gate・divider・duty・Triangle DAC・LFSR を独立再構成する。</summary>
    internal sealed class NesRegisterTraceChip : IRegisterTraceChip
    {
        private const int StatusAddress = 0x4015;
        private const int DmcControlAddress = 0x4010;
        private const int DmcOutputAddress = 0x4011;
        private const int FrameCounterAddress = 0x4017;
        private const int LastChannelAddress = 0x400F;
        private const int ImmediateFrameClock = 0xC0;
        private const int DisabledSweep = 0x08;
        private const int LinearMask = 0x7F;
        private const int ChannelCount = 4;
        private const int TriangleChannel = 2;
        private const int NoiseChannel = 3;
        private const int RegisterBase = 0x4000;
        private const int ChannelStride = 4;
        private const int LengthValue = 10;
        private const int VolumeMask = 0x0F;
        private const int HighMask = 7;
        private const int ByteBits = 8;
        private const int DutyShift = 6;
        private const int PulseSteps = 8;
        private const int MinimumPulseTimer = 8;
        private const int MaximumTimer = 2047;
        private const int PulseClockDivider = 2;
        private const int NoiseShortTap = 6;
        private const int TriangleSteps = 32;
        private const int TrianglePeak = 15;
        private const int FeedbackBit = 14;
        private const int ShortMode = 0x80;
        private const int ConstantHeld = 0x30;
        private static readonly int[] DutyPatterns = { 0x02, 0x06, 0x1E, 0xF9 };
        private static readonly int[] NoisePeriods =
            { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };
        private readonly int[] _timers = new int[ChannelCount];
        private readonly int[] _remaining = { 1, 1, 1, 1 };
        private readonly int[] _positions = new int[ChannelCount];
        private readonly int[] _lengths = new int[ChannelCount];
        private readonly int[] _controls = new int[ChannelCount];
        private readonly bool[] _sweepNegated = new bool[TriangleChannel];
        private int _enabled;
        private int _linearReload;
        private int _linear;
        private bool _linearPending;
        private int _noiseRegister;

        /// <summary>NTSC NES の CPU クロック。</summary>
        public int ClockRate => 1789773;
        /// <summary>比較用の線形モノラル和。実機の非線形ミキサーは契約に含めない。</summary>
        public (double Left, double Right) Output
        {
            get
            {
                double value = Level(0) + Level(1) + Level(TriangleChannel) + Level(NoiseChannel);
                return (value, value);
            }
        }

        internal int NoiseState { get; private set; } = 1;
        internal int NoisePeriod => NoisePeriods[_noiseRegister & VolumeMask];
        internal int Timer(int channel) => _timers[channel];
        internal int Position(int channel) => _positions[channel];
        internal int RemainingCycles(int channel) => _remaining[channel];
        internal bool IsGated(int channel) => _lengths[channel] > 0 && (channel != TriangleChannel || _linear > 0);

        internal int Level(int channel)
        {
            if (channel == TriangleChannel)
            {
                int position = _positions[channel];
                return position <= TrianglePeak ? TrianglePeak - position : position - (TrianglePeak + 1);
            }
            if (!IsGated(channel))
            {
                return 0;
            }
            if (channel == NoiseChannel)
            {
                return (NoiseState & 1) == 0 ? _controls[channel] & VolumeMask : 0;
            }
            int pattern = DutyPatterns[_controls[channel] >> DutyShift];
            bool isHigh = ((pattern >> _positions[channel]) & 1) != 0;
            bool sweepOverflows = !_sweepNegated[channel] && _timers[channel] * 2 > MaximumTimer;
            return _timers[channel] >= MinimumPulseTimer && !sweepOverflows && isHigh ? _controls[channel] & VolumeMask : 0;
        }

        /// <summary>生成サブセット以外を拒否し、書き込み順に副作用を適用する。</summary>
        public void Apply(int address, int value)
        {
            Require(value is >= 0 and <= byte.MaxValue);
            if (address == StatusAddress)
            {
                Require((value & ~VolumeMask) == 0);
                _enabled = value;
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    if ((value & (1 << channel)) == 0)
                    {
                        _lengths[channel] = 0;
                    }
                }
                return;
            }
            if (address is DmcControlAddress or DmcOutputAddress)
            {
                Require(value == 0);
                return;
            }
            if (address == FrameCounterAddress)
            {
                Require(value == ImmediateFrameClock);
                if (_linearPending)
                {
                    _linear = _linearReload;
                }
                return;
            }
            Require(address >= RegisterBase && address <= LastChannelAddress);
            int channelIndex = (address - RegisterBase) / ChannelStride;
            int offset = (address - RegisterBase) % ChannelStride;
            ApplyChannel(channelIndex, offset, value);
        }

        private void ApplyChannel(int channel, int offset, int value)
        {
            switch (offset)
            {
                case 0:
                    if (channel == TriangleChannel)
                    {
                        Require(value == byte.MaxValue);
                        _linearReload = value & LinearMask;
                        return;
                    }
                    Require((value & ConstantHeld) == ConstantHeld);
                    _controls[channel] = value;
                    break;
                case 1:
                    Require(channel < TriangleChannel && value == DisabledSweep);
                    _sweepNegated[channel] = true;
                    break;
                case 2:
                    if (channel == NoiseChannel)
                    {
                        Require((value & ~(ShortMode | VolumeMask)) == 0);
                        _noiseRegister = value;
                        return;
                    }
                    _timers[channel] = (_timers[channel] & (HighMask << ByteBits)) | value;
                    break;
                case 3:
                    Require((value & ~HighMask) == 0);
                    if ((_enabled & (1 << channel)) != 0)
                    {
                        _lengths[channel] = LengthValue;
                    }
                    if (channel == NoiseChannel)
                    {
                        Require(value == 0);
                        return;
                    }
                    _timers[channel] = (_timers[channel] & byte.MaxValue) | (value << ByteBits);
                    if (channel == TriangleChannel)
                    {
                        _linearPending = true;
                        return;
                    }
                    // high は sequencer だけを戻す。動作中の divider は保持する。
                    _positions[channel] = 0;
                    break;
            }
        }

        /// <summary>divider の残時間を保持して整数 CPU cycles を進める。</summary>
        public void AdvanceCycles(int cycles)
        {
            Require(cycles >= 0);
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                int remainingCycles = cycles;
                while (remainingCycles >= _remaining[channel])
                {
                    remainingCycles -= _remaining[channel];
                    ClockSequencer(channel);
                    _remaining[channel] = Period(channel);
                }
                _remaining[channel] -= remainingCycles;
            }
        }

        private int Period(int channel)
        {
            if (channel == NoiseChannel)
            {
                return NoisePeriod;
            }
            return (_timers[channel] + 1) * (channel == TriangleChannel ? 1 : PulseClockDivider);
        }

        private void ClockSequencer(int channel)
        {
            if (channel == NoiseChannel)
            {
                int tap = (_noiseRegister & ShortMode) == 0 ? 1 : NoiseShortTap;
                int feedback = (NoiseState ^ (NoiseState >> tap)) & 1;
                NoiseState = (NoiseState >> 1) | (feedback << FeedbackBit);
                return;
            }
            if (channel == TriangleChannel && !IsGated(channel))
            {
                return;
            }
            int steps = channel == TriangleChannel ? TriangleSteps : PulseSteps;
            _positions[channel] = (_positions[channel] + 1) % steps;
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("NES 再合成器の検証サブセット外です。");
            }
        }
    }
}
