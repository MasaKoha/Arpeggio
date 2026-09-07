using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Render
{
    /// <summary>独立リーダーによる WAV ヘッダーと PCM 量子化の往復を検証する。</summary>
    public sealed class WavWriterTests
    {
        private const int StereoChannels = 2;
        private const int PcmBits = 16;
        private const double PcmScale = 32768;
        private const double QuantizationTolerance = 2 / PcmScale;

        /// <summary>指定レートの 16 bit ステレオ PCM と左右順序を保持する。</summary>
        [Theory]
        [InlineData(22050)]
        [InlineData(44100)]
        [InlineData(48000)]
        public void Write_StreamRoundTripsFormatAndSamples(int sampleRate)
        {
            float[] source = new float[] { -1, 1, -0.75f, 0.25f, 0, -0.5f, 0.5f, 0.75f };
            using var stream = new MemoryStream();
            WavWriter.Write(stream, source, sampleRate);
            Assert.True(stream.CanWrite);
            stream.Position = 0;
            var result = new WavReader(stream);

            Assert.Equal(sampleRate, result.SampleRate);
            Assert.Equal(StereoChannels, result.Channels);
            Assert.Equal(PcmBits, result.BitsPerSample);
            Assert.Equal(source.Length, result.Samples.Length);
            for (int index = 0; index < source.Length; index++)
            {
                double restored = result.Samples[index] / PcmScale;
                Assert.InRange(Math.Abs(restored - source[index]), 0, QuantizationTolerance);
            }
        }

        /// <summary>実際のソング書き出しをファイル API から読み戻せる。</summary>
        [Fact]
        public void Write_PathRoundTripsRenderedSong()
        {
            const int SampleRate = 48000;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arpeggio-wave-" + Guid.NewGuid().ToString("N") + ".wav");
            Song song = TestSongFactory.CreateActiveSong(ChipKind.Nes, 48);
            float[] samples = new SongRenderer(song, new RenderSettings(SampleRate, 1, 0.1)).RenderAll();
            try
            {
                WavWriter.Write(path, samples, SampleRate);
                using var stream = File.OpenRead(path);
                var result = new WavReader(stream);

                Assert.Equal(SampleRate, result.SampleRate);
                Assert.Equal(samples.Length, result.Samples.Length);
                Assert.Contains(result.Samples, sample => sample != 0);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>空の波形でも有効な空の PCM チャンクを出力する。</summary>
        [Fact]
        public void Write_EmptySamplesProduceValidEmptyWave()
        {
            const int SampleRate = 44100;
            using var stream = new MemoryStream();
            WavWriter.Write(stream, ReadOnlySpan<float>.Empty, SampleRate);
            stream.Position = 0;
            var result = new WavReader(stream);

            Assert.Empty(result.Samples);
            Assert.Equal(SampleRate, result.SampleRate);
        }
    }
}
