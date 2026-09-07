using System;
using System.IO;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>既存の量子化テストを Core の WAV リーダーへ接続する薄いアダプター。</summary>
    internal sealed class WavReader
    {
        private const float NegativePcmScale = 32768;
        private const float PositivePcmScale = 32767;
        internal int SampleRate { get; }
        internal int Channels => 2;
        internal int BitsPerSample => 16;
        internal short[] Samples { get; }

        internal WavReader(Stream stream)
        {
            float[] samples = Arpeggio.Core.Render.WavReader.Read(stream, out int sampleRate);
            SampleRate = sampleRate;
            Samples = new short[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                float scale = samples[index] < 0 ? NegativePcmScale : PositivePcmScale;
                Samples[index] = (short)Math.Round(samples[index] * scale);
            }
        }
    }
}
