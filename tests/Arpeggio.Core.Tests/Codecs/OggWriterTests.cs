using System;
using System.IO;
using System.Text;
using Arpeggio.Codecs;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Import;
using Xunit;

namespace Arpeggio.Core.Tests.Codecs
{
    /// <summary>Vorbis のコンテナ出力・圧縮・入力検証と Stream 所有権を検証する。</summary>
    public sealed class OggWriterTests
    {
        private const int SampleRate = 44100;
        private const int StereoChannels = 2;

        /// <summary>同じ三秒の音声を WAV より小さな OggS 形式で保存する。</summary>
        [Theory]
        [InlineData(-0.1f)]
        [InlineData(0.5f)]
        [InlineData(1f)]
        public void Write_CompressesStereoAudioAndLeavesStreamOpen(float quality)
        {
            const int DurationSeconds = 3;
            const double Frequency = 440;
            float[] samples = new float[SampleRate * DurationSeconds * StereoChannels];
            for (int index = 0; index < samples.Length / StereoChannels; index++)
            {
                float value = (float)(0.5 * Math.Sin(2 * Math.PI * Frequency * index / SampleRate));
                samples[index * StereoChannels] = value;
                samples[index * StereoChannels + 1] = -value;
            }
            using MemoryStream wave = new MemoryStream();
            using MemoryStream ogg = new MemoryStream();
            WavWriter.Write(wave, samples, SampleRate);
            OggWriter.Write(ogg, samples, SampleRate, quality);
            Assert.True(ogg.CanWrite);
            byte[] bytes = ogg.ToArray();
            Assert.Equal("OggS", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.True(ogg.Length < wave.Length);
            AssertContainsEndOfStream(bytes);
        }

        /// <summary>
        /// OggVorbisEncoder 1.2.2 は 32 kHz 以上のエンコード後に 32 kHz 未満・品質 0.5 未満で静的テーブル汚染による例外を起こす。
        /// AssemblyLoadContext 隔離で回避していることを、同一プロセス内の順序依存で確認する。
        /// </summary>
        [Fact]
        public void Write_LowRateAfterHighRateDoesNotThrow()
        {
            float[] pcm = new float[SampleRate * StereoChannels];
            using MemoryStream high = new MemoryStream();
            OggWriter.Write(high, pcm, 44100, 0.5f);
            using MemoryStream low = new MemoryStream();
            OggWriter.Write(low, pcm, 22050, 0.4f);
            using MemoryStream lowest = new MemoryStream();
            OggWriter.Write(lowest, pcm, 8000, -0.1f);
            AssertContainsEndOfStream(low.ToArray());
            AssertContainsEndOfStream(lowest.ToArray());
        }

        /// <summary>端数バッファと空入力でも EOS を持つコンテナを閉じる。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1025)]
        public void Write_FinishesEmptyAndPartialBlocks(int frames)
        {
            using MemoryStream stream = new MemoryStream();
            OggWriter.Write(stream, new float[frames * StereoChannels], SampleRate);
            AssertContainsEndOfStream(stream.ToArray());
        }

        /// <summary>無効品質・非有限 PCM・端数フレーム・不正レートで出力を変更しない。</summary>
        [Fact]
        public void Write_RejectsInvalidInputBeforeTouchingOutput()
        {
            using SampleFileFixture files = new SampleFileFixture();
            const string Original = "keep";
            File.WriteAllText(files.OggPath, Original);
            Assert.Throws<ArgumentOutOfRangeException>(() => OggWriter.Write(files.OggPath, Array.Empty<float>(), SampleRate, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => OggWriter.Write(files.OggPath, Array.Empty<float>(), SampleRate, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => OggWriter.Write(files.OggPath, Array.Empty<float>(), SampleRate, -0.2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => OggWriter.Write(files.OggPath, Array.Empty<float>(), SampleRate, 1.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => OggWriter.Write(files.OggPath, Array.Empty<float>(), 0));
            Assert.Throws<ArgumentException>(() => OggWriter.Write(files.OggPath, new float[1], SampleRate));
            Assert.Throws<ArgumentException>(() => OggWriter.Write(files.OggPath, new float[] { 0, float.NaN }, SampleRate));
            Assert.Equal(Original, File.ReadAllText(files.OggPath));
            using MemoryStream readOnly = new MemoryStream(Array.Empty<byte>(), writable: false);
            Assert.Throws<ArgumentException>(() => OggWriter.Write(readOnly, Array.Empty<float>(), SampleRate));
        }

        private static void AssertContainsEndOfStream(byte[] bytes)
        {
            const int HeaderBytes = 27;
            const int SegmentCountOffset = 26;
            const int HeaderFlagsOffset = 5;
            const int EndOfStreamFlag = 4;
            int offset = 0;
            bool ended = false;
            while (offset < bytes.Length)
            {
                Assert.True(bytes.Length - offset >= HeaderBytes);
                Assert.Equal("OggS", Encoding.ASCII.GetString(bytes, offset, 4));
                int segments = bytes[offset + SegmentCountOffset];
                int bodyLength = 0;
                for (int index = 0; index < segments; index++)
                {
                    bodyLength += bytes[offset + HeaderBytes + index];
                }
                ended = (bytes[offset + HeaderFlagsOffset] & EndOfStreamFlag) != 0;
                offset += HeaderBytes + segments + bodyLength;
            }
            Assert.Equal(bytes.Length, offset);
            Assert.True(ended);
        }
    }
}
