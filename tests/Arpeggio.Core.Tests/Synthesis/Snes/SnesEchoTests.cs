using Arpeggio.Core.Document;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>左右独立のディレイ時刻、フィードバックとリセットを検証する。</summary>
    public sealed class SnesEchoTests
    {
        /// <summary>インパルスが指定の遅延後に左右独立で減衰反復する。</summary>
        [Fact]
        public void Process_DelaysImpulseAndFeedsBackPerChannel()
        {
            const int SampleRate = 1000;
            const int DelayMilliseconds = 16;
            const int DelaySamples = 16;
            var echo = new SnesEcho(SampleRate, new SnesEchoSettings
            {
                DelayMilliseconds = DelayMilliseconds,
                Feedback = 0.5,
                Volume = 0.5
            });
            for (int position = 0; position <= DelaySamples * 2; position++)
            {
                float leftInput = position == 0 ? 1 : 0;
                echo.Process(leftInput, 0, out float left, out float right);
                Assert.Equal(0f, right);
                float expected = 0;
                if (position == DelaySamples)
                {
                    expected = 0.5f;
                }
                else if (position == DelaySamples * 2)
                {
                    expected = 0.25f;
                }

                Assert.Equal(expected, left);
            }
        }

        /// <summary>リセット後に以前の送り音が漏れない。</summary>
        [Fact]
        public void Reset_ClearsDelayHistory()
        {
            const int ObservationSamples = 64;
            var echo = new SnesEcho(1000, new SnesEchoSettings { DelayMilliseconds = 16, Feedback = 0.5, Volume = 1 });
            echo.Process(1, -1, out _, out _);
            echo.Reset();
            for (int index = 0; index < ObservationSamples; index++)
            {
                echo.Process(0, 0, out float left, out float right);
                Assert.Equal(0f, left);
                Assert.Equal(0f, right);
            }
        }
    }
}
