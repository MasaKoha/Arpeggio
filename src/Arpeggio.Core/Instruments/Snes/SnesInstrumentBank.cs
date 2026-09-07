using System;
using Arpeggio.Core.Synthesis.Snes;

namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>決定的な合成レシピから BRR 往復済みの短い素材を作る純関数。</summary>
    public static class SnesInstrumentBank
    {
        /// <summary>全素材で固定のサンプルレート。</summary>
        public const int SampleRate = 32000;
        private const double PeakAmplitude = 0.72;

        /// <summary>新しい BRR キャッシュを返す。推奨値は Catalog.Get で取得する。</summary>
        public static BrrSample Build(string preset)
        {
            SnesInstrumentPreset definition = SnesInstrumentCatalog.Get(preset);
            float[] samples = new float[definition.SampleCount];
            if (definition.Loop)
            {
                SnesInstrumentRecipeSustained.Fill(preset, samples);
            }
            else if (definition.Category == "減衰系")
            {
                SnesInstrumentRecipeDecay.Fill(preset, samples);
            }
            else
            {
                SnesInstrumentRecipeDrums.Fill(preset, samples);
            }
            Normalize(samples);
            return BrrSample.Create(samples, definition.Loop, definition.LoopStart, definition.LoopEnd);
        }

        private static void Normalize(float[] samples)
        {
            double sum = 0;
            foreach (float sample in samples)
            {
                sum += sample;
            }
            double mean = sum / samples.Length;
            double peak = 0;
            foreach (float sample in samples)
            {
                peak = Math.Max(peak, Math.Abs(sample - mean));
            }
            double scale = peak == 0 ? 0 : PeakAmplitude / peak;
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (float)((samples[index] - mean) * scale);
            }
        }
    }
}
