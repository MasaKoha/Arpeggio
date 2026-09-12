using System;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Export.GameBoy
{
    /// <summary>手書きの DMG レジスタから再合成器自身の duty・RAM・DAC・LFSR を検証する。</summary>
    public sealed class GameBoyRegisterTraceChipTests
    {
        private const int PulseStepCycles = 1192;
        private const int WaveStepCycles = 298;
        private const int FullVolume = 15;

        /// <summary>両 Pulse の duty は一周期の固定ビット列に一致し、再 trigger では位相を保持する。</summary>
        [Theory]
        [InlineData(0, 0, new int[] { 0, 0, 0, 0, 0, 0, 0, 1 })]
        [InlineData(0, 1, new int[] { 1, 0, 0, 0, 0, 0, 0, 1 })]
        [InlineData(0, 2, new int[] { 1, 0, 0, 0, 0, 1, 1, 1 })]
        [InlineData(0, 3, new int[] { 0, 1, 1, 1, 1, 1, 1, 0 })]
        [InlineData(1, 0, new int[] { 0, 0, 0, 0, 0, 0, 0, 1 })]
        [InlineData(1, 1, new int[] { 1, 0, 0, 0, 0, 0, 0, 1 })]
        [InlineData(1, 2, new int[] { 1, 0, 0, 0, 0, 1, 1, 1 })]
        [InlineData(1, 3, new int[] { 0, 1, 1, 1, 1, 1, 1, 0 })]
        public void DutyAndRetriggerHaveExactIndependentState(int channel, int duty, int[] expected)
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            int address = 0xFF11 + channel * 5;
            chip.Apply(address, duty << 6);
            chip.Apply(address + 1, 0xF0);
            chip.Apply(address + 2, 0xD6);
            chip.Apply(address + 3, 0x86);
            Assert.Equal(1750, chip.Frequency(channel));
            Assert.Equal(0, chip.Level(channel));
            chip.AdvanceCycles(PulseStepCycles * 8);
            foreach (int level in expected)
            {
                Assert.Equal(level * FullVolume, chip.Level(channel));
                chip.AdvanceCycles(PulseStepCycles);
            }
            chip.AdvanceCycles(PulseStepCycles + 100);
            int position = chip.Position(channel);
            chip.Apply(address + 3, 0x86);
            Assert.Equal(position, chip.Position(channel));
            Assert.Equal(PulseStepCycles, chip.RemainingCycles(channel));
            Assert.Equal(2, chip.Triggers(channel));
        }

        /// <summary>DAC off は即停止し、DAC on だけでは発音せず、新周期と音量は trigger で反映する。</summary>
        [Fact]
        public void DacOffAndTriggerAreDistinctAndPitchWriteKeepsDivider()
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            chip.Apply(0xFF12, 0xF0);
            chip.Apply(0xFF13, 0xD6);
            chip.Apply(0xFF14, 0x86);
            chip.AdvanceCycles(100);
            int remaining = chip.RemainingCycles(0);
            chip.Apply(0xFF13, 0xD7);
            chip.Apply(0xFF14, 6);
            Assert.Equal(remaining, chip.RemainingCycles(0));
            Assert.Equal(1, chip.Triggers(0));
            chip.Apply(0xFF12, 0);
            Assert.False(chip.IsActive(0));
            Assert.False(chip.DacEnabled(0));
            chip.Apply(0xFF14, 0x86);
            Assert.False(chip.IsActive(0));
            chip.Apply(0xFF12, 0x50);
            Assert.False(chip.IsActive(0));
            chip.Apply(0xFF14, 0x86);
            Assert.True(chip.IsActive(0));
            Assert.Equal(1188, chip.RemainingCycles(0));
        }

        /// <summary>Wave RAM の全ニブル順序と全音量段階を、右シフトした一周期の整数値で照合する。</summary>
        [Theory]
        [InlineData(0x00, 0)] [InlineData(0x60, 2)] [InlineData(0x40, 1)] [InlineData(0x20, 0)]
        public void WaveRamAndVolumeDecodeExactly(int volumeRegister, int shift)
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            for (int position = 0; position < 16; position++)
            {
                int first = position * 2 % 16;
                chip.Apply(0xFF30 + position, (first << 4) | (first + 1));
            }
            chip.Apply(0xFF1C, volumeRegister);
            chip.Apply(0xFF1D, 0x6B);
            chip.Apply(0xFF1A, 0x80);
            chip.Apply(0xFF1E, 0x87);
            Assert.Equal(1899, chip.Frequency(2));
            for (int position = 0; position < 32; position++)
            {
                Assert.Equal(position % 16, chip.WaveSample(position));
                Assert.Equal(volumeRegister == 0 ? 0 : (position % 16) >> shift, chip.Level(2));
                chip.AdvanceCycles(WaveStepCycles);
            }
            Assert.Throws<InvalidOperationException>(() => chip.Apply(0xFF30, 0xFF));
            int triggers = chip.Triggers(2);
            chip.Apply(0xFF1C, 0x60);
            Assert.Equal(triggers, chip.Triggers(2));
            chip.Apply(0xFF1A, 0);
            chip.Apply(0xFF30, 0xF1);
            Assert.Equal(15, chip.WaveSample(0));
            Assert.Equal(1, chip.WaveSample(1));
            Assert.False(chip.IsActive(2));
        }

        /// <summary>Noise の XNOR／zero seed 遷移は両幅の固定値に一致し、trigger だけが状態を戻す。</summary>
        [Theory]
        [InlineData(0, new int[] { 0x4000, 0x6000, 0x7000, 0x7800, 0x7C00, 0x7E00 })]
        [InlineData(8, new int[] { 0x4040, 0x6060, 0x7070, 0x7878, 0x7C7C, 0x7E7E })]
        public void NoiseFeedbackAndRetriggerMatchFixedStates(int widthBit, int[] expected)
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            chip.Apply(0xFF21, 0xF0);
            chip.Apply(0xFF22, widthBit);
            chip.Apply(0xFF23, 0x80);
            Assert.Equal(0, chip.NoiseState);
            foreach (int state in expected)
            {
                chip.AdvanceCycles(8);
                Assert.Equal(state, chip.NoiseState);
            }
            int held = chip.NoiseState;
            chip.Apply(0xFF22, widthBit | 0x10);
            Assert.Equal(held, chip.NoiseState);
            chip.Apply(0xFF23, 0x80);
            Assert.Equal(0, chip.NoiseState);
            Assert.Equal(16, chip.RemainingCycles(3));
        }

        /// <summary>NR43 の全 divisor と端の shift は CPU cycles の固定値を持つ。</summary>
        [Theory]
        [InlineData(0x00, 8)] [InlineData(0x01, 16)] [InlineData(0x02, 32)] [InlineData(0x03, 48)]
        [InlineData(0x04, 64)] [InlineData(0x05, 80)] [InlineData(0x06, 96)] [InlineData(0x07, 112)]
        [InlineData(0x10, 16)] [InlineData(0xD0, 65536)] [InlineData(0xD7, 917504)]
        public void NoiseDivisorAndShiftControlClockBoundary(int value, int expectedPeriod)
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            chip.Apply(0xFF21, 0xF0);
            chip.Apply(0xFF22, value);
            chip.Apply(0xFF23, 0x80);
            Assert.Equal(expectedPeriod, chip.NoisePeriod);
            chip.AdvanceCycles(expectedPeriod - 1);
            Assert.Equal(0, chip.NoiseState);
            chip.AdvanceCycles(1);
            Assert.Equal(0x4000, chip.NoiseState);
        }

        /// <summary>NR51 は左右を独立選択し、NR50 のゼロ設定も倍率 1/8 を維持する。</summary>
        [Fact]
        public void RoutingAndMasterVolumeUseIndependentLeftRightGains()
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            chip.Apply(0xFF11, 0x80);
            chip.Apply(0xFF12, 0xF0);
            chip.Apply(0xFF14, 0x80);
            chip.AdvanceCycles(8192 * 8);
            chip.Apply(0xFF25, 0x10);
            Assert.Equal((15.0, 0.0), chip.Output);
            chip.Apply(0xFF25, 1);
            Assert.Equal((0.0, 15.0), chip.Output);
            chip.Apply(0xFF25, 0x11);
            chip.Apply(0xFF24, 0);
            Assert.Equal((15.0 / 8, 15.0 / 8), chip.Output);
            chip.Apply(0xFF26, 0);
            Assert.Equal((0.0, 0.0), chip.Output);
            Assert.False(chip.IsActive(0));
        }

        /// <summary>未対応 sweep・envelope・length・停止 Noise shift・未知アドレスを検出する。</summary>
        [Theory]
        [InlineData(0xFF10, 1)] [InlineData(0xFF12, 0xF1)] [InlineData(0xFF14, 0xC0)]
        [InlineData(0xFF22, 0xE0)] [InlineData(0xFF22, 0xF0)] [InlineData(0xFF27, 0)]
        public void UnsupportedRegistersFailExplicitly(int address, int value)
        {
            GameBoyRegisterTraceChip chip = PowerOn();
            Assert.Throws<InvalidOperationException>(() => chip.Apply(address, value));
        }

        private static GameBoyRegisterTraceChip PowerOn()
        {
            var chip = new GameBoyRegisterTraceChip();
            chip.Apply(0xFF26, 0);
            chip.Apply(0xFF26, 0x80);
            chip.Apply(0xFF24, 0x77);
            return chip;
        }
    }
}
