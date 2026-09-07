using Arpeggio.Core.Sequencing;
using Xunit;

namespace Arpeggio.Core.Tests.Sequencing
{
    /// <summary>端数を含むサンプルレートでも 60 Hz 境界が累積ずれしないことを検証する。</summary>
    public sealed class FrameClockTests
    {
        /// <summary>境界直前と境界時刻でフレーム番号が一度だけ変化する。</summary>
        [Theory]
        [InlineData(44100)]
        [InlineData(48000)]
        [InlineData(22050)]
        public void GetNextBoundary_AdvancesExactlySixtyFramesPerSecond(int sampleRate)
        {
            const int FrameRate = 60;
            var clock = new FrameClock(sampleRate);
            long position = 0;
            Assert.Equal(0L, clock.GetFrame(position));
            for (int frame = 1; frame <= FrameRate; frame++)
            {
                long boundary = clock.GetNextBoundary(position);
                Assert.True(boundary > position);
                Assert.Equal((long)frame - 1, clock.GetFrame(boundary - 1));
                Assert.Equal((long)frame, clock.GetFrame(boundary));
                position = boundary;
            }

            Assert.Equal((long)sampleRate, position);
        }
    }
}
