using System;
using System.IO;

namespace Arpeggio.Codecs
{
    /// <summary>ステレオ float PCM を Ogg Vorbis の VBR 音声へ書き出す。</summary>
    public static class OggWriter
    {
        private const int StereoChannels = 2;
        private const float MinimumQuality = -0.1f;
        private const float MaximumQuality = 1;
        private const float DefaultQuality = 0.5f;

        /// <summary>入力を検証後にファイルを作成し、Vorbis 音声を書き出す。</summary>
        public static void Write(string path, ReadOnlySpan<float> interleavedStereo, int sampleRate, float quality = DefaultQuality)
        {
            Validate(interleavedStereo, sampleRate, quality);
            using FileStream stream = File.Create(path);
            IsolatedVorbisEncoder.Encode(stream, interleavedStereo.ToArray(), sampleRate, quality);
        }

        /// <summary>呼び出し側所有のストリームへ書き出す。ストリームは閉じない。</summary>
        public static void Write(Stream stream, ReadOnlySpan<float> interleavedStereo, int sampleRate, float quality = DefaultQuality)
        {
            Validate(interleavedStereo, sampleRate, quality);
            if (!stream.CanWrite)
            {
                throw new ArgumentException("書き込み可能なストリームを指定してください。", nameof(stream));
            }
            IsolatedVorbisEncoder.Encode(stream, interleavedStereo.ToArray(), sampleRate, quality);
        }

        private static void Validate(ReadOnlySpan<float> samples, int sampleRate, float quality)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "sampleRate は正の整数です。");
            }
            if (float.IsNaN(quality) || quality < MinimumQuality || quality > MaximumQuality)
            {
                throw new ArgumentOutOfRangeException(nameof(quality), "quality は有限の -0.1〜1 です。");
            }
            if (samples.Length % StereoChannels != 0)
            {
                throw new ArgumentException("PCM は左右一組のステレオです。", nameof(samples));
            }
            foreach (float sample in samples)
            {
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                {
                    throw new ArgumentException("PCM は有限値で指定してください。", nameof(samples));
                }
            }
        }
    }
}
