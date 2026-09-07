using System;
using System.Buffers.Binary;

namespace Arpeggio.Core.Instruments
{
    /// <summary>埋め込みモノラル PCM と保存用 Base64 の純粋な変換。</summary>
    public static class SampleDataCodec
    {
        /// <summary>音色一つに埋め込める PCM の最大バイト数（2 MiB）。</summary>
        public const int MaximumByteCount = 2 * 1024 * 1024;
        /// <summary>PCM 16 bit 一サンプルのバイト数。</summary>
        public const int BytesPerSample = sizeof(short);
        private const int MaximumEncodedLength = (MaximumByteCount + 2) / 3 * 4;
        private const float NegativeScale = 32768;
        private const float PositiveScale = 32767;

        /// <summary>little-endian PCM 16 bit の Base64 を復元し、長さと上限を検証する。</summary>
        public static short[] Decode(string sampleData)
        {
            if (sampleData.Length == 0 || sampleData.Length > MaximumEncodedLength)
            {
                throw new ArgumentException("サンプルは空にできず、PCM で 2 MiB 以下です。", nameof(sampleData));
            }
            byte[] bytes = Convert.FromBase64String(sampleData);
            if (bytes.Length == 0 || bytes.Length > MaximumByteCount || bytes.Length % BytesPerSample != 0)
            {
                throw new ArgumentException("サンプルは空でない PCM 16 bit、2 MiB 以下です。", nameof(sampleData));
            }
            short[] samples = new short[bytes.Length / BytesPerSample];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * BytesPerSample, BytesPerSample));
            }
            return samples;
        }

        /// <summary>PCM 16 bit をプラットフォーム非依存の little-endian Base64 にする。</summary>
        public static string Encode(ReadOnlySpan<short> samples)
        {
            if (samples.IsEmpty || samples.Length > MaximumByteCount / BytesPerSample)
            {
                throw new ArgumentException("サンプルは空にできず、PCM で 2 MiB 以下です。", nameof(samples));
            }
            byte[] bytes = new byte[samples.Length * BytesPerSample];
            for (int index = 0; index < samples.Length; index++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * BytesPerSample, BytesPerSample), samples[index]);
            }
            return Convert.ToBase64String(bytes);
        }

        /// <summary>正負の飽和端点を ±1 に対応させて再生用 float にする。</summary>
        public static float[] ToFloat(ReadOnlySpan<short> samples)
        {
            float[] converted = new float[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                converted[index] = samples[index] < 0 ? samples[index] / NegativeScale : samples[index] / PositiveScale;
            }
            return converted;
        }

        /// <summary>有限の float を ±1 に制限し、PCM 16 bit へ四捨五入する。</summary>
        public static short[] FromFloat(ReadOnlySpan<float> samples)
        {
            short[] converted = new short[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                float value = samples[index];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentException("サンプルは有限値で指定してください。", nameof(samples));
                }
                value = Math.Clamp(value, -1, 1);
                converted[index] = (short)Math.Round(value * (value < 0 ? NegativeScale : PositiveScale), MidpointRounding.AwayFromZero);
            }
            return converted;
        }
    }
}
