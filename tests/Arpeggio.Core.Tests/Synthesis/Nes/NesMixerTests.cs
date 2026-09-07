using System;
using Arpeggio.Core.Synthesis.Nes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Nes
{
    /// <summary>設計書の非線形 DAC 応答が各チャンネル群へ適用されることを検証する。</summary>
    public sealed class NesMixerTests
    {
        /// <summary>正振幅の Pulse と TND が明示式の期待値に一致する。</summary>
        [Fact]
        public void Mix_PositiveSignalsMatchHardwareResponse()
        {
            const double MaximumLevel = 15;
            const double PulseScale = 95.88;
            const double PulseDivisor = 8128;
            const double TndScale = 159.79;
            const double TriangleDivisor = 8227;
            const double NoiseDivisor = 12241;
            const double DpcmDivisor = 22638;
            const double ResponseOffset = 100;
            const double Tolerance = 0.000001;
            double pulseSum = MaximumLevel * (1 + 0.5);
            double weighted = MaximumLevel * (0.25 / TriangleDivisor + 0.75 / NoiseDivisor + 0.5 / DpcmDivisor);
            double expected = PulseScale / (PulseDivisor / pulseSum + ResponseOffset)
                + TndScale / (1 / weighted + ResponseOffset);

            float actual = NesMixer.Mix(1, 0.5f, 0.25f, 0.75f, 0.5f);

            Assert.InRange(Math.Abs(actual - expected), 0, Tolerance);
            Assert.Equal(0f, NesMixer.Mix(0, 0, 0, 0, 0));
        }

        /// <summary>符号を反転しても非線形応答の振幅を保つ。</summary>
        [Fact]
        public void Mix_NegativeSignalsAreSymmetric()
        {
            float positive = NesMixer.Mix(1, 1, 1, 1, 0);
            float negative = NesMixer.Mix(-1, -1, -1, -1, 0);

            Assert.Equal(-positive, negative);
        }
    }
}
