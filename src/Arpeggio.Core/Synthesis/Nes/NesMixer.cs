using System;

namespace Arpeggio.Core.Synthesis.Nes
{
    /// <summary>NES の Pulse と TND の非線形応答を符号付き振幅へ適用する。</summary>
    public static class NesMixer
    {
        private const double MaximumLevel = 15;
        private const double PulseScale = 95.88;
        private const double PulseDivisor = 8128;
        private const double TndScale = 159.79;
        private const double TriangleDivisor = 8227;
        private const double NoiseDivisor = 12241;
        private const double DpcmDivisor = 22638;
        private const double ResponseOffset = 100;

        /// <summary>規定順の五チャンネルを非線形に合成する。</summary>
        public static float Mix(float pulseOne, float pulseTwo, float triangle, float noise, float dpcm)
        {
            double positive = MixPositive(Math.Max(0, pulseOne), Math.Max(0, pulseTwo), Math.Max(0, triangle), Math.Max(0, noise), Math.Max(0, dpcm));
            double negative = MixPositive(Math.Max(0, -pulseOne), Math.Max(0, -pulseTwo), Math.Max(0, -triangle), Math.Max(0, -noise), Math.Max(0, -dpcm));
            return (float)(positive - negative);
        }

        private static double MixPositive(double pulseOne, double pulseTwo, double triangle, double noise, double dpcm)
        {
            double pulses = (pulseOne + pulseTwo) * MaximumLevel;
            double weighted = MaximumLevel * (triangle / TriangleDivisor + noise / NoiseDivisor + dpcm / DpcmDivisor);
            double pulseOutput = PulseScale * pulses / (PulseDivisor + ResponseOffset * pulses);
            double tndOutput = TndScale * weighted / (1 + ResponseOffset * weighted);
            return pulseOutput + tndOutput;
        }
    }
}
