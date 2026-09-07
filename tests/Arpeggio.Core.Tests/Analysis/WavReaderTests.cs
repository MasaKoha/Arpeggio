using System;
using System.IO;
using System.Text;
using Arpeggio.Core.Analysis;
using Xunit;
using CoreWavReader = Arpeggio.Core.Render.WavReader;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>書き出し実装を使わず構成した RIFF で入力検証とモノラル変換を確認する。</summary>
    public sealed class WavReaderTests
    {
        private const int SampleRate = 22050;
        private const int FormatLength = 16;
        private const int PcmBits = 16;
        private const int PcmFormat = 1;
        private const int BytesPerSample = 2;
        private const int RiffSizeOffset = 4;
        private const int RiffSizeAdjustment = 8;

        /// <summary>モノラルは左右に複製し、ステレオは左右の順序を保つ。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Read_PcmPreservesChannelsAndLeavesStreamOpen(int channels)
        {
            short[] input = { short.MinValue, short.MaxValue, -16384, 8192 };
            using MemoryStream stream = CreateWave(channels, input);
            float[] samples = CoreWavReader.Read(stream, out int sampleRate);
            Assert.Equal(SampleRate, sampleRate);
            Assert.Equal(input.Length / channels * 2, samples.Length);
            Assert.True(stream.CanRead);
            for (int index = 0; index < input.Length; index++)
            {
                float expected = input[index] < 0 ? input[index] / 32768f : input[index] / 32767f;
                int destination = channels == 1 ? index * 2 : index;
                Assert.Equal(expected, samples[destination]);
                if (channels == 1)
                {
                    Assert.Equal(expected, samples[destination + 1]);
                }
            }
        }

        /// <summary>未知の奇数長チャンクと data が fmt より先にある配置を扱う。</summary>
        [Fact]
        public void Read_SkipsPaddedChunksAndAcceptsDataBeforeFormat()
        {
            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                WriteHeader(writer);
                WriteTag(writer, "JUNK");
                writer.Write(3);
                writer.Write(new byte[] { 1, 2, 3, 0 });
                WriteData(writer, new short[] { -16384, 16384 });
                WriteFormat(writer, 1, PcmBits, PcmFormat);
                CompleteWave(writer);
            }
            float[] samples = CoreWavReader.Read(stream, out int sampleRate);
            Assert.Equal(SampleRate, sampleRate);
            Assert.Equal(new float[] { -0.5f, -0.5f, 16384f / 32767, 16384f / 32767 }, samples);
        }

        /// <summary>不対応の形式・ビット深度・チャンネル数を明示的に拒否する。</summary>
        [Theory]
        [InlineData(1, 8, 1)]
        [InlineData(1, 24, 1)]
        [InlineData(2, 32, 3)]
        [InlineData(3, 16, 1)]
        public void Read_RejectsUnsupportedFormat(int channels, int bits, int format)
        {
            using MemoryStream stream = CreateWave(channels, Array.Empty<short>(), bits, format);
            Assert.Throws<InvalidDataException>(() => CoreWavReader.Read(stream, out _));
        }

        /// <summary>RIFF の長さ、フレーム境界、必須チャンク欠落を拒否する。</summary>
        [Fact]
        public void Read_RejectsMalformedRiff()
        {
            using MemoryStream partialFrame = CreateWave(2, new short[] { 1 });
            Assert.Throws<InvalidDataException>(() => CoreWavReader.Read(partialFrame, out _));
            using MemoryStream truncated = CreateWave(2, new short[] { 1, 2 });
            truncated.SetLength(truncated.Length - 1);
            Assert.Throws<InvalidDataException>(() => CoreWavReader.Read(truncated, out _));
            using MemoryStream missingChunks = new MemoryStream(Encoding.ASCII.GetBytes("RIFF\u0004\u0000\u0000\u0000WAVE"));
            Assert.Throws<InvalidDataException>(() => CoreWavReader.Read(missingChunks, out _));
            using MemoryStream shortHeader = new MemoryStream(new byte[3]);
            Assert.Throws<InvalidDataException>(() => CoreWavReader.Read(shortHeader, out _));
        }

        /// <summary>宣言長が RIFF 外へ伸びるチャンクを読み込まない。</summary>
        [Fact]
        public void Read_RejectsOversizedChunk()
        {
            using MemoryStream stream = CreateWave(2, new short[] { 1, 2 });
            const int DataLengthOffset = 40;
            stream.Position = DataLengthOffset;
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(uint.MaxValue);
            }
            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() => CoreWavReader.Read(stream, out _));
        }

        /// <summary>16 bit の正負端点を両方クリップとして解析できる。</summary>
        [Fact]
        public void Read_PreservesBothClippingRails()
        {
            using MemoryStream stream = CreateWave(2, new short[] { short.MinValue, short.MaxValue });
            float[] samples = CoreWavReader.Read(stream, out int sampleRate);
            AnalysisReport report = AudioAnalyzer.Analyze(samples, sampleRate, new AnalysisSettings());
            Assert.Equal(new float[] { -1, 1 }, samples);
            Assert.Equal(2, report.ClippedSampleCount);
        }

        private static MemoryStream CreateWave(int channels, short[] samples, int bits = PcmBits, int format = PcmFormat)
        {
            MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            WriteHeader(writer);
            WriteFormat(writer, channels, bits, format);
            WriteData(writer, samples);
            CompleteWave(writer);
            return stream;
        }

        private static void WriteHeader(BinaryWriter writer)
        {
            WriteTag(writer, "RIFF");
            writer.Write(0);
            WriteTag(writer, "WAVE");
        }

        private static void WriteFormat(BinaryWriter writer, int channels, int bits, int format)
        {
            WriteTag(writer, "fmt ");
            writer.Write(FormatLength);
            writer.Write((short)format);
            writer.Write((short)channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * channels * bits / 8);
            writer.Write((short)(channels * bits / 8));
            writer.Write((short)bits);
        }

        private static void WriteData(BinaryWriter writer, short[] samples)
        {
            WriteTag(writer, "data");
            writer.Write(samples.Length * BytesPerSample);
            foreach (short sample in samples)
            {
                writer.Write(sample);
            }
        }

        private static void CompleteWave(BinaryWriter writer)
        {
            Stream stream = writer.BaseStream;
            stream.Position = RiffSizeOffset;
            writer.Write((int)stream.Length - RiffSizeAdjustment);
            stream.Position = 0;
        }

        private static void WriteTag(BinaryWriter writer, string tag)
        {
            writer.Write(Encoding.ASCII.GetBytes(tag));
        }
    }
}
