using System;

namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>一周期に収まる倍音合成。境界を固定して短い BRR ループに適合させる。</summary>
    internal static class SnesInstrumentRecipeSustained
    {
        private const int FluteNoiseSeed = 17011;
        private const int HarmonicCount = 12;
        private const double DetuneRatio = 0.004;
        private const double PulseDuty = 0.25;
        private const double BreathAmplitude = 0.006;
        private const double BrassPhaseMotion = 0.025;
        private const double StringsHarmonicRolloff = 1.35;
        private const double BrassHarmonicRolloff = 1.15;
        private const double BrassEvenHarmonicAmplitude = 0.12;
        private const double FluteSecondHarmonicAmplitude = 0.045;
        private const double BassSecondHarmonicAmplitude = 0.16;
        private static readonly double[] OrganHarmonics = { 1, 0.35, 0.2, 0.12 };
        private static readonly double[] ChoirHarmonics = { 1, 0, 0.05, 0.2, 0.035 };

        internal static void Fill(string preset, float[] samples)
        {
            Random random = new Random(FluteNoiseSeed);
            for (int index = 0; index < samples.Length; index++)
            {
                double phase = 2 * Math.PI * index / samples.Length;
                double value = 0;
                for (int harmonic = 1; harmonic <= HarmonicCount; harmonic++)
                {
                    value += ReadHarmonic(preset, phase, harmonic);
                }
                if (preset == "flute")
                {
                    value += (random.NextDouble() * 2 - 1) * BreathAmplitude * Math.Sin(phase / 2);
                }
                samples[index] = (float)value;
            }
        }

        private static double ReadHarmonic(string preset, double phase, int harmonic)
        {
            double angle = phase * harmonic;
            switch (preset)
            {
                case "strings":
                    // 短周期ループの継ぎ目を保つため、二声の位相差を周期末で滑らかに戻す。
                    double detune = DetuneRatio * harmonic * Math.Sin(phase);
                    return Math.Sin(angle + detune) * Math.Cos(detune) / Math.Pow(harmonic, StringsHarmonicRolloff);
                case "brass":
                    double strength = harmonic % 2 == 0 ? BrassEvenHarmonicAmplitude : 1.0;
                    return strength * Math.Sin(angle + BrassPhaseMotion * Math.Sin(phase)) / Math.Pow(harmonic, BrassHarmonicRolloff);
                case "organ":
                    return harmonic <= OrganHarmonics.Length ? OrganHarmonics[harmonic - 1] * Math.Sin(angle) : 0;
                case "choir":
                    return harmonic <= ChoirHarmonics.Length ? ChoirHarmonics[harmonic - 1] * Math.Sin(angle) : 0;
                case "flute":
                    if (harmonic == 1)
                    {
                        return Math.Sin(angle);
                    }
                    return harmonic == 2 ? FluteSecondHarmonicAmplitude * Math.Sin(angle) : 0;
                case "lead":
                    return Math.Sin(Math.PI * harmonic * PulseDuty) * Math.Cos(angle - Math.PI * harmonic * PulseDuty) / harmonic;
                case "bass":
                    if (harmonic == 2)
                    {
                        return BassSecondHarmonicAmplitude * Math.Sin(angle);
                    }
                    return harmonic % 2 == 1 ? Math.Sin(angle) / (harmonic * harmonic) : 0;
                default:
                    throw new ArgumentException("持続系プリセットではありません。", nameof(preset));
            }
        }
    }
}
