using System;
using Arpeggio.Core.Sequencing;
using Xunit;

namespace Arpeggio.Core.Tests.Sequencing
{
    /// <summary>テンポからサンプル時刻への変換と丸め誤差を検証する。</summary>
    public sealed class TickClockTests
    {
        /// <summary>150 BPM の 4 小節が 6.4 秒に一致する。</summary>
        [Theory]
        [InlineData(44100)]
        [InlineData(48000)]
        public void TickToSamples_FourBarsMatchMusicalDuration(int sampleRate)
        {
            const int Tempo = 150;
            const int FourBarTicks = 768;
            const double ExpectedSeconds = 6.4;
            var clock = new TickClock(Tempo, sampleRate);

            Assert.Equal((long)(ExpectedSeconds * sampleRate), clock.TickToSamples(FourBarTicks));
            Assert.Equal((double)FourBarTicks, clock.SamplesToTick(clock.TickToSamples(FourBarTicks)), precision: 9);
        }

        /// <summary>割り切れないテンポでも往復誤差が半サンプル以内に収まる。</summary>
        [Fact]
        public void Conversion_RoundTripHasAtMostHalfSampleError()
        {
            const int Tempo = 137;
            const int SampleRate = 44100;
            const int TicksPerBeat = 48;
            const int LastTick = 10000;
            const double FloatingTolerance = 0.000000001;
            double halfSampleTicks = Tempo * TicksPerBeat / (60.0 * SampleRate) / 2;
            var clock = new TickClock(Tempo, SampleRate);
            for (int tick = 0; tick <= LastTick; tick++)
            {
                double restored = clock.SamplesToTick(clock.TickToSamples(tick));
                Assert.InRange(Math.Abs(restored - tick), 0, halfSampleTicks + FloatingTolerance);
            }
        }
    }
}
