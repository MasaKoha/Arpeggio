using System;
using System.IO;
using System.Text;

namespace Arpeggio.Core.Render
{
    /// <summary>インターリーブ済みステレオ PCM を 16 bit WAV に保存する。</summary>
    public static class WavWriter
    {
        private const int ChannelCount = 2;
        private const int BitsPerSample = 16;
        private const int BytesPerSample = BitsPerSample / 8;
        private const int FormatChunkSize = 16;
        private const int RiffOverhead = 36;
        private const short PcmFormat = 1;
        private const float ClipEpsilon = 0.0000001f;

        /// <summary>パスに WAV を保存し、生成したストリームを破棄する。</summary>
        public static void Write(string path, ReadOnlySpan<float> interleavedStereo, int sampleRate)
        {
            Validate(interleavedStereo, sampleRate);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Write(stream, interleavedStereo, sampleRate);
        }

        /// <summary>呼び出し側のストリームを閉じずに WAV を書く。</summary>
        public static void Write(Stream stream, ReadOnlySpan<float> interleavedStereo, int sampleRate)
        {
            Validate(interleavedStereo, sampleRate);
            int dataBytes = checked(interleavedStereo.Length * BytesPerSample);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(checked(dataBytes + RiffOverhead));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(FormatChunkSize);
            writer.Write(PcmFormat);
            writer.Write((short)ChannelCount);
            writer.Write(sampleRate);
            writer.Write(checked(sampleRate * ChannelCount * BytesPerSample));
            writer.Write((short)(ChannelCount * BytesPerSample));
            writer.Write((short)BitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            for (int index = 0; index < interleavedStereo.Length; index++)
            {
                writer.Write(ToPcm(interleavedStereo[index]));
            }
        }

        private static short ToPcm(float sample)
        {
            if (sample <= -1 + ClipEpsilon)
            {
                return short.MinValue;
            }
            if (sample >= 1 - ClipEpsilon)
            {
                return short.MaxValue;
            }
            return (short)Math.Round(sample * short.MaxValue, MidpointRounding.AwayFromZero);
        }

        private static void Validate(ReadOnlySpan<float> samples, int sampleRate)
        {
            if (sampleRate <= 0 || sampleRate > int.MaxValue / (ChannelCount * BytesPerSample) || samples.Length % ChannelCount != 0)
            {
                throw new ArgumentException("サンプルレートは正、PCM は左右一組の長さにしてください。");
            }
            if ((long)samples.Length * BytesPerSample + RiffOverhead > int.MaxValue)
            {
                throw new ArgumentException("WAV のサイズ上限を超えています。");
            }
            for (int index = 0; index < samples.Length; index++)
            {
                if (float.IsNaN(samples[index]) || float.IsInfinity(samples[index]))
                {
                    throw new ArgumentException("PCM に非有限値があります。");
                }
            }
        }
    }
}
