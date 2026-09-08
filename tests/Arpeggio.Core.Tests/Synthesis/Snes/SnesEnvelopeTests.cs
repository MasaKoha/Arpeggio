using System;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>DSP ADSR のレジスタ境界、更新時刻と秒指定の互換量子化。</summary>
    public sealed class SnesEnvelopeTests
    {
        /// <summary>最大 attack は二サンプルで立ち上がり、最大 sustain は減衰しない。</summary>
        [Fact]
        public void FastAttack_ReachesMaximumAndHolds()
        {
            SnesEnvelope envelope = new SnesEnvelope();
            envelope.Start(new SnesAdsrRegisters(15, 7, 7, 0));
            Assert.InRange(envelope.ReadSample(), 0.5, 0.501);
            Assert.Equal(1, envelope.ReadSample());
            for (int index = 0; index < 32000; index++)
            {
                Assert.Equal(1, envelope.ReadSample());
            }
            envelope.Release();
            for (int index = 0; index < 255; index++)
            {
                Assert.True(envelope.ReadSample() > 0);
            }
            Assert.Equal(0, envelope.ReadSample());
            Assert.True(envelope.IsSilent);
        }

        /// <summary>最遅 attack はレート表の周期を過ぎるまで音量を更新しない。</summary>
        [Fact]
        public void SlowAttack_UsesDspSamplePeriod()
        {
            SnesEnvelope envelope = new SnesEnvelope();
            envelope.Start(new SnesAdsrRegisters(0, 0, 0, 0));
            for (int index = 0; index < 2047; index++)
            {
                Assert.Equal(0, envelope.ReadSample());
            }
            Assert.Equal(32.0 / 2047, envelope.ReadSample());
        }

        /// <summary>秒数を長くすると attack と decay のレジスタ速度は速くならない。</summary>
        [Fact]
        public void Quantize_LongerSecondsNeverChooseFasterRates()
        {
            int previousAttack = 15;
            int previousDecay = 7;
            for (int index = 0; index <= 1000; index++)
            {
                double seconds = index / 100.0;
                SnesAdsrRegisters registers = SnesEnvelope.Quantize(new AdsrEnvelope(seconds, seconds, 0.5, 1));
                Assert.InRange(registers.Attack, 0, previousAttack);
                Assert.InRange(registers.Decay, 0, previousDecay);
                Assert.Equal(3, registers.SustainLevel);
                Assert.Equal(0, registers.SustainRate);
                previousAttack = registers.Attack;
                previousDecay = registers.Decay;
            }
        }

        /// <summary>保持減衰は停止レートと最速レートで異なり、ゼロへ到達する。</summary>
        [Fact]
        public void SustainRate_ControlsExponentialDecay()
        {
            SnesEnvelope envelope = new SnesEnvelope();
            envelope.Start(new SnesAdsrRegisters(15, 7, 7, 31));
            envelope.ReadSample();
            envelope.ReadSample();
            Assert.Equal(2039.0 / 2047, envelope.ReadSample());
            for (int index = 0; index < 2048; index++)
            {
                envelope.ReadSample();
            }
            Assert.True(envelope.IsSilent);
        }
    }
}
