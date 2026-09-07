using System;

namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>固定シードのノイズと位相連続のピッチ下降から打楽器を作る。</summary>
    internal static class SnesInstrumentRecipeDrums
    {
        private const int KickNoiseSeed = 27011;
        private const int SnareNoiseSeed = 27017;
        private const int HatNoiseSeed = 27031;
        private const int OpenHatNoiseSeed = 27043;
        private const int TomNoiseSeed = 27059;
        private const int CrashNoiseSeed = 27061;
        private const double KickStartFrequency = 150;
        private const double KickEndFrequency = 50;
        private const double KickSweepSeconds = 0.06;
        private const double TomStartFrequency = 280;
        private const double TomEndFrequency = 120;
        private const double SnareFrequency = 200;
        private const double ClickSeconds = 0.002;
        private const double TailFadeSeconds = 0.01;
        private const double EnvelopeDecay = 6;
        private const double ClickAmplitude = 0.025;
        private const double SnareNoiseAmplitude = 0.65;
        private const double SnareToneAmplitude = 0.35;

        internal static void Fill(string preset, float[] samples)
        {
            int seed = preset switch
            {
                "kick" => KickNoiseSeed, "snare" => SnareNoiseSeed,
                "hat" => HatNoiseSeed, "openhat" => OpenHatNoiseSeed,
                "tom" => TomNoiseSeed, "crash" => CrashNoiseSeed,
                _ => throw new ArgumentException("ドラムプリセットではありません。", nameof(preset))
            };
            Random random = new Random(seed);
            double previousNoise = 0;
            double previousDifference = 0;
            double phase = 0;
            double duration = samples.Length / (double)SnesInstrumentBank.SampleRate;
            for (int index = 0; index < samples.Length; index++)
            {
                double seconds = index / (double)SnesInstrumentBank.SampleRate;
                double noise = random.NextDouble() * 2 - 1;
                double difference = noise - previousNoise;
                double highNoise = difference - previousDifference;
                previousNoise = noise;
                previousDifference = difference;
                double value = ReadBody(preset, seconds, noise, highNoise, ref phase);
                double envelope = Math.Exp(-EnvelopeDecay * seconds / duration);
                double tail = Math.Min(1, (duration - seconds) / TailFadeSeconds);
                samples[index] = (float)(value * envelope * tail);
            }
        }

        private static double ReadBody(string preset, double seconds, double noise, double highNoise, ref double phase)
        {
            if (preset == "kick" || preset == "tom")
            {
                double start = preset == "kick" ? KickStartFrequency : TomStartFrequency;
                double end = preset == "kick" ? KickEndFrequency : TomEndFrequency;
                double frequency = start + (end - start) * Math.Min(1, seconds / KickSweepSeconds);
                phase += 2 * Math.PI * frequency / SnesInstrumentBank.SampleRate;
                return Math.Sin(phase) + ClickAmplitude * noise * Math.Exp(-seconds / ClickSeconds);
            }
            if (preset == "snare")
            {
                return SnareNoiseAmplitude * noise + SnareToneAmplitude * Math.Sin(2 * Math.PI * SnareFrequency * seconds);
            }
            return highNoise;
        }
    }
}
