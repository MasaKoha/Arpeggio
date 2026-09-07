using System;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>外部素材に依存しない一周期の内蔵サンプルを生成する。</summary>
    public static class SnesWaveformBuilder
    {
        /// <summary>線形補間用の標準一周期サンプル数。</summary>
        public const int DefaultLength = 2048;
        private const int MinimumLength = 2;
        private const int NoiseRegisterWidth = 15;
        private const double PulseDuty = 0.25;
        private const double SquareDuty = 0.5;

        /// <summary>指定した波形の一周期を生成する。オーディオ処理の前に呼ぶ。</summary>
        public static float[] Build(SnesWaveformKind kind, int length = DefaultLength)
        {
            if (length < MinimumLength)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if ((int)kind < (int)SnesWaveformKind.Sine || (int)kind > (int)SnesWaveformKind.Noise)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            var samples = new float[length];
            int noiseRegister = 1;
            for (int index = 0; index < length; index++)
            {
                double phase = (double)index / length;
                int feedback = (noiseRegister ^ (noiseRegister >> 1)) & 1;
                noiseRegister = (noiseRegister >> 1) | (feedback << (NoiseRegisterWidth - 1));
                samples[index] = (float)(kind switch
                {
                    SnesWaveformKind.Sine => Math.Sin(2 * Math.PI * phase),
                    SnesWaveformKind.Square => phase < SquareDuty ? 1 : -1,
                    SnesWaveformKind.Saw => 2 * phase - 1,
                    SnesWaveformKind.Triangle => 1 - 4 * Math.Abs(phase - 0.5),
                    SnesWaveformKind.Pulse => phase < PulseDuty ? 1 : -1,
                    SnesWaveformKind.Noise => (noiseRegister & 1) == 0 ? 1 : -1,
                    _ => 0
                });
            }
            return samples;
        }
    }
}
