using System;

namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>倍音別の減衰速度で打弦・撥弦・金属音を作る。</summary>
    internal static class SnesInstrumentRecipeDecay
    {
        private const double RootFrequency = 261.6255653005986;
        private const int HarmonicCount = 12;
        private const double TailFadeSeconds = 0.02;
        private const double AttackSeconds = 0.001;
        private const double PianoBodyDecay = 3.8;
        private const double PianoTransientDecay = 24;
        private const double PianoUpperHarmonicDecay = 8;
        private const double PianoBodyWeight = 0.65;
        private const double PianoTransientWeight = 1 - PianoBodyWeight;
        private const double PluckDecay = 9;
        private const double BellDecay = 3;
        private const double StringHarmonicRolloff = 1.3;
        private static readonly double[] BellRatios = { 1, 2.76, 5.4 };
        private static readonly double[] BellAmplitudes = { 1, 0.38, 0.18 };

        internal static void Fill(string preset, float[] samples)
        {
            for (int index = 0; index < samples.Length; index++)
            {
                double seconds = index / (double)SnesInstrumentBank.SampleRate;
                double phase = 2 * Math.PI * RootFrequency * seconds;
                double value = preset == "bell" ? ReadBell(phase, seconds) : ReadString(preset, phase, seconds);
                double tail = (samples.Length - 1 - index) / (SnesInstrumentBank.SampleRate * TailFadeSeconds);
                double fade = Math.Min(1, seconds / AttackSeconds) * Math.Min(1, tail);
                samples[index] = (float)(value * fade);
            }
        }

        private static double ReadString(string preset, double phase, double seconds)
        {
            double value = 0;
            for (int harmonic = 1; harmonic <= HarmonicCount; harmonic++)
            {
                double decay = preset == "piano"
                    ? ReadPianoDecay(harmonic, seconds)
                    : Math.Exp(-PluckDecay * Math.Sqrt(harmonic) * seconds);
                value += Math.Sin(phase * harmonic) * decay / Math.Pow(harmonic, StringHarmonicRolloff);
            }
            return value;
        }

        private static double ReadPianoDecay(int harmonic, double seconds)
        {
            double bodyRate = PianoBodyDecay + (harmonic - 1) * PianoUpperHarmonicDecay;
            double body = PianoBodyWeight * Math.Exp(-bodyRate * seconds);
            double transient = PianoTransientWeight * Math.Exp(-PianoTransientDecay * harmonic * seconds);
            return body + transient;
        }

        private static double ReadBell(double phase, double seconds)
        {
            double value = 0;
            for (int partialIndex = 0; partialIndex < BellRatios.Length; partialIndex++)
            {
                value += Math.Sin(phase * BellRatios[partialIndex]) * BellAmplitudes[partialIndex]
                    * Math.Exp(-BellDecay * (partialIndex + 1) * seconds);
            }
            return value;
        }
    }
}
