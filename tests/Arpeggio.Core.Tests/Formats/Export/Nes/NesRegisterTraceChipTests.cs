using System;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Export.Nes
{
    /// <summary>手書きレジスタ列で NES 再合成器自身の状態遷移を検証する。</summary>
    public sealed class NesRegisterTraceChipTests
    {
        private const int PulseStepCycles = 508;
        private const int TriangleStepCycles = 127;
        private const int FullVolume = 15;

        /// <summary>低音では sweep 初期化の省略が target overflow による消音として現れる。</summary>
        [Theory]
        [InlineData(0)] [InlineData(1)]
        public void LowPulseRequiresNegatedDisabledSweep(int channel)
        {
            var chip = new NesRegisterTraceChip();
            int address = 0x4000 + channel * 4;
            chip.Apply(0x4015, 1 << channel);
            chip.Apply(address, 0xBF);
            chip.Apply(address + 2, 0xF1);
            chip.Apply(address + 3, 7);
            chip.AdvanceCycles(1);
            Assert.Equal(2033, chip.Timer(channel));
            Assert.Equal(0, chip.Level(channel));
            chip.Apply(address + 1, 0x08);
            Assert.Equal(FullVolume, chip.Level(channel));
        }

        /// <summary>両 Pulse の全 duty は、実機の一周期のビット列と完全一致する。</summary>
        [Theory]
        [InlineData(0, 0, new int[] { 0, 1, 0, 0, 0, 0, 0, 0 })]
        [InlineData(0, 1, new int[] { 0, 1, 1, 0, 0, 0, 0, 0 })]
        [InlineData(0, 2, new int[] { 0, 1, 1, 1, 1, 0, 0, 0 })]
        [InlineData(0, 3, new int[] { 1, 0, 0, 1, 1, 1, 1, 1 })]
        [InlineData(1, 0, new int[] { 0, 1, 0, 0, 0, 0, 0, 0 })]
        [InlineData(1, 1, new int[] { 0, 1, 1, 0, 0, 0, 0, 0 })]
        [InlineData(1, 2, new int[] { 0, 1, 1, 1, 1, 0, 0, 0 })]
        [InlineData(1, 3, new int[] { 1, 0, 0, 1, 1, 1, 1, 1 })]
        public void PulseDutyMatchesEveryStep(int channel, int duty, int[] expected)
        {
            var chip = new NesRegisterTraceChip();
            int address = 0x4000 + channel * 4;
            chip.Apply(0x4015, 1 << channel);
            chip.Apply(address, 0x3F | (duty << 6));
            chip.Apply(address + 2, 0xFD);
            chip.Apply(address + 3, 0);
            chip.AdvanceCycles(1);
            chip.Apply(address + 3, 0);
            Assert.Equal(253, chip.Timer(channel));
            foreach (int level in expected)
            {
                Assert.Equal(level * FullVolume, chip.Level(channel));
                chip.AdvanceCycles(PulseStepCycles);
            }
            Assert.Equal(0, chip.Position(channel));
        }

        /// <summary>enable 前の length load は無効で、enable だけでは再発音しない。</summary>
        [Fact]
        public void LengthLoadRequiresEnableAndPreservesOtherVoices()
        {
            var chip = new NesRegisterTraceChip();
            chip.Apply(0x4003, 0);
            chip.Apply(0x4015, 3);
            Assert.False(chip.IsGated(0));
            chip.Apply(0x4003, 0);
            chip.Apply(0x4007, 0);
            Assert.True(chip.IsGated(0));
            Assert.True(chip.IsGated(1));
            chip.Apply(0x4015, 2);
            Assert.False(chip.IsGated(0));
            Assert.True(chip.IsGated(1));
            chip.Apply(0x4015, 3);
            Assert.False(chip.IsGated(0));
        }

        /// <summary>同値 high は duty のみを戻し、low・high の更新は divider の残時間を保持する。</summary>
        [Fact]
        public void PulseHighRestartsSequencerWithoutResettingDivider()
        {
            var chip = new NesRegisterTraceChip();
            chip.Apply(0x4002, 0xFD);
            chip.Apply(0x4003, 0);
            chip.AdvanceCycles(100);
            Assert.Equal(1, chip.Position(0));
            int remaining = chip.RemainingCycles(0);
            chip.Apply(0x4002, 0xFE);
            Assert.Equal(1, chip.Position(0));
            chip.Apply(0x4003, 0);
            Assert.Equal(0, chip.Position(0));
            Assert.Equal(remaining, chip.RemainingCycles(0));
            chip.AdvanceCycles(remaining - 1);
            Assert.Equal(0, chip.Position(0));
            chip.AdvanceCycles(1);
            Assert.Equal(1, chip.Position(0));
            Assert.Equal(510, chip.RemainingCycles(0));
        }

        /// <summary>Triangle は linear load 後にだけ進み、Off と再 On は DAC 位相をリセットしない。</summary>
        [Fact]
        public void TriangleNeedsLinearClockAndHoldsDacAfterStop()
        {
            var chip = new NesRegisterTraceChip();
            chip.Apply(0x4015, 4);
            chip.Apply(0x4008, 0xFF);
            chip.Apply(0x400A, 0x7E);
            chip.Apply(0x400B, 0);
            chip.AdvanceCycles(1);
            Assert.False(chip.IsGated(2));
            Assert.Equal(15, chip.Level(2));
            chip.Apply(0x4017, 0xC0);
            Assert.True(chip.IsGated(2));
            for (int step = 0; step < 32; step++)
            {
                int expected = step < 16 ? 15 - step : step - 16;
                Assert.Equal(expected, chip.Level(2));
                chip.AdvanceCycles(TriangleStepCycles);
            }
            chip.AdvanceCycles(TriangleStepCycles * 3);
            chip.Apply(0x4015, 0);
            int held = chip.Level(2);
            chip.AdvanceCycles(TriangleStepCycles * 40);
            Assert.Equal(held, chip.Level(2));
            chip.Apply(0x4015, 4);
            chip.Apply(0x400B, 0);
            chip.Apply(0x4017, 0xC0);
            Assert.Equal(held, chip.Level(2));
        }

        /// <summary>両 Noise mode の手計算 seed 遷移が一致し、length の再ロードで seed を戻さない。</summary>
        [Theory]
        [InlineData(0, new int[] { 0x4000, 0x2000, 0x1000, 0x0800, 0x0400, 0x0200, 0x0100, 0x0080, 0x0040, 0x0020 })]
        [InlineData(0x80, new int[] { 0x4000, 0x2000, 0x1000, 0x0800, 0x0400, 0x0200, 0x0100, 0x0080, 0x0040, 0x4020 })]
        public void NoiseUsesIndependentFeedbackAndKeepsSeed(int mode, int[] expected)
        {
            var chip = new NesRegisterTraceChip();
            chip.Apply(0x400E, mode);
            chip.AdvanceCycles(1);
            foreach (int state in expected)
            {
                Assert.Equal(state, chip.NoiseState);
                chip.Apply(0x4015, 8);
                chip.Apply(0x400F, 0);
                Assert.Equal(state, chip.NoiseState);
                chip.AdvanceCycles(4);
            }
        }

        /// <summary>全 NTSC Noise period の値と、更新直前／直後の境界が固定表に一致する。</summary>
        [Theory]
        [InlineData(0, 4)] [InlineData(1, 8)] [InlineData(2, 16)] [InlineData(3, 32)]
        [InlineData(4, 64)] [InlineData(5, 96)] [InlineData(6, 128)] [InlineData(7, 160)]
        [InlineData(8, 202)] [InlineData(9, 254)] [InlineData(10, 380)] [InlineData(11, 508)]
        [InlineData(12, 762)] [InlineData(13, 1016)] [InlineData(14, 2034)] [InlineData(15, 4068)]
        public void NoisePeriodControlsExactClockBoundary(int selection, int period)
        {
            var chip = new NesRegisterTraceChip();
            chip.Apply(0x400E, selection);
            chip.AdvanceCycles(1);
            Assert.Equal(period, chip.NoisePeriod);
            chip.AdvanceCycles(period - 1);
            Assert.Equal(0x4000, chip.NoiseState);
            chip.AdvanceCycles(1);
            Assert.Equal(0x2000, chip.NoiseState);
        }

        /// <summary>DPCM・未知レジスタ・未対応 envelope／length／sweep を黙って無視しない。</summary>
        [Theory]
        [InlineData(0x4015, 0x10)] [InlineData(0x4012, 0)] [InlineData(0x4000, 0x0F)]
        [InlineData(0x4003, 8)] [InlineData(0x4001, 0)] [InlineData(0x4017, 0x40)]
        public void UnsupportedRegistersFailExplicitly(int address, int value)
        {
            var chip = new NesRegisterTraceChip();
            Assert.Throws<InvalidOperationException>(() => chip.Apply(address, value));
        }
    }
}
