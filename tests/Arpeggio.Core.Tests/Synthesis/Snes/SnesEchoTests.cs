using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Analysis;
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
            const int SampleRate = 32000;
            const int DelayMilliseconds = 16;
            const int DelaySamples = 512;
            var echo = new SnesEcho(SampleRate, new SnesEchoSettings
            {
                DelayMilliseconds = DelayMilliseconds,
                Feedback = 0.5,
                Volume = 0.5
            });
            for (int position = 0; position <= DelaySamples * 2; position++)
            {
                float leftInput = position == 0 ? 0.5f : 0;
                echo.Process(leftInput, 0, out float left, out float right);
                Assert.Equal(0f, right);
                float expected = 0;
                if (position == DelaySamples)
                {
                    expected = 0.5f * 127 / 128 * 0.5f;
                }
                else if (position == DelaySamples * 2)
                {
                    expected = 0.5f * 127 / 128 * 0.5f * 127 / 128 * 0.5f;
                }

                Assert.InRange(Math.Abs(expected - left), 0, 1.0 / 32768);
            }
        }

        /// <summary>8 kHz のエコー成分は LowPass で Flat より小さくなる。</summary>
        [Fact]
        public void Fir_LowPassAttenuatesHighFrequencyEcho()
        {
            const int SampleRate = 32000;
            const int SampleCount = 8192;
            SnesEcho flat = new SnesEcho(SampleRate, new SnesEchoSettings
            {
                DelayMilliseconds = 16, Volume = 1, FirCoefficients = SnesEchoFirPresets.Flat
            });
            SnesEcho lowPass = new SnesEcho(SampleRate, new SnesEchoSettings
            {
                DelayMilliseconds = 16, Volume = 1, FirCoefficients = SnesEchoFirPresets.LowPass
            });
            float[] flatSamples = new float[SampleCount];
            float[] lowPassSamples = new float[SampleCount];
            for (int index = 0; index < SampleCount; index++)
            {
                float input = (float)(0.5 * Math.Sin(2 * Math.PI * 8000 * index / SampleRate));
                flat.Process(input, 0, out flatSamples[index], out _);
                lowPass.Process(input, 0, out lowPassSamples[index], out _);
            }
            Assert.True(SignalAnalysis.RootMeanSquare(lowPassSamples.AsSpan(1024)) < SignalAnalysis.RootMeanSquare(flatSamples.AsSpan(1024)) * 0.1);
        }

        /// <summary>係数はコンストラクタで複製し、以後の設定編集から履歴を分離する。</summary>
        [Fact]
        public void Fir_CoefficientsAreSnapshottedAndZeroDelayIsDisabled()
        {
            SnesEchoSettings settings = new SnesEchoSettings { DelayMilliseconds = 16, Volume = 1 };
            SnesEcho echo = new SnesEcho(32000, settings);
            settings.FirCoefficients[0] = 0;
            echo.Process(0.5f, 0, out _, out _);
            for (int index = 1; index < 512; index++)
            {
                echo.Process(0, 0, out _, out _);
            }
            echo.Process(0, 0, out float wet, out _);
            Assert.Equal(0.5f * 127 / 128, wet);
            SnesEcho disabled = new SnesEcho(32000, new SnesEchoSettings { Volume = 1 });
            disabled.Process(1, 1, out float left, out float right);
            Assert.Equal(0f, left);
            Assert.Equal(0f, right);
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
