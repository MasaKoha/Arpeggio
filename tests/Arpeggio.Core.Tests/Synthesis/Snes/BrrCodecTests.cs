using System;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>BRR の誤差、ヘッダ、予測履歴、ブロック境界を検証する。</summary>
    public sealed class BrrCodecTests
    {
        /// <summary>2048 サンプルの正弦波を元信号 RMS の 1 % 以内で往復する。</summary>
        [Fact]
        public void RoundTrip_SineHasLessThanOnePercentRelativeError()
        {
            const int SampleCount = 2048;
            const double Frequency = 440;
            float[] source = new float[SampleCount];
            for (int index = 0; index < source.Length; index++)
            {
                source[index] = (float)(0.8 * Math.Sin(2 * Math.PI * Frequency * index / 32000));
            }
            byte[] encoded = BrrCodec.Encode(source, true);
            float[] decoded = BrrCodec.Decode(encoded, out bool loop, out bool end);
            double errorPower = 0;
            double sourcePower = 0;
            for (int index = 0; index < source.Length; index++)
            {
                double difference = source[index] - decoded[index];
                errorPower += difference * difference;
                sourcePower += source[index] * source[index];
            }
            Assert.InRange(Math.Sqrt(errorPower / sourcePower), 0, 0.01);
            Assert.True(loop);
            Assert.True(end);
            Assert.Equal(SampleCount / 16 * 9, encoded.Length);
            Assert.Equal(encoded, BrrCodec.Encode(source, true));
        }

        /// <summary>部分ブロックを切り上げ、終端フラグは最終ブロックだけに付ける。</summary>
        [Theory]
        [InlineData(1, 1)]
        [InlineData(16, 1)]
        [InlineData(17, 2)]
        [InlineData(33, 3)]
        public void Encode_PadsBlocksAndMarksOnlyLastHeader(int sampleCount, int blockCount)
        {
            byte[] encoded = BrrCodec.Encode(new float[sampleCount]);
            Assert.Equal(blockCount * 9, encoded.Length);
            for (int blockIndex = 0; blockIndex < blockCount - 1; blockIndex++)
            {
                Assert.Equal(0, encoded[blockIndex * 9] & 3);
            }
            Assert.Equal(1, encoded[(blockCount - 1) * 9] & 3);
            float[] decoded = BrrCodec.Decode(encoded, out bool loop, out bool end);
            Assert.False(loop);
            Assert.True(end);
            Assert.Equal(blockCount * 16, decoded.Length);
        }

        /// <summary>別ブロックの過去二点を各フィルタが引き継ぐ。</summary>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 3840)]
        [InlineData(2, 3968)]
        [InlineData(3, 4032)]
        public void Decode_PredictorsCarryHistoryAcrossBlocks(int filter, int expected)
        {
            byte[] encoded = new byte[18];
            encoded[0] = 0xC0;
            Array.Fill(encoded, (byte)0x11, 1, 8);
            encoded[9] = (byte)((filter << 2) | 1);
            float[] decoded = BrrCodec.Decode(encoded);
            Assert.Equal(4096f / 32768, decoded[15]);
            Assert.Equal(expected / 32768f, decoded[16]);
        }

        /// <summary>ヘッダの shift と符号付きニブルを独立した既知値で確認する。</summary>
        [Fact]
        public void Decode_UsesSignedNibblesAndShift()
        {
            byte[] encoded = new byte[9];
            encoded[0] = 0xC3;
            encoded[1] = 0x78;
            float[] decoded = BrrCodec.Decode(encoded, out bool loop, out bool end);
            Assert.Equal(28672f / 32768, decoded[0]);
            Assert.Equal(-1f, decoded[1]);
            Assert.True(loop && end);
            Assert.Throws<ArgumentException>(() => BrrCodec.Decode(new byte[8]));
            Assert.Throws<ArgumentException>(() => BrrCodec.Encode(new[] { float.NaN }));
        }

        /// <summary>PCM の長さを保持し、再生ループだけを BRR 境界へ丸める。</summary>
        [Fact]
        public void Sample_QuantizesLoopToBlockBoundaries()
        {
            BrrSample sample = BrrSample.Create(new float[50], true, 19, 47);
            Assert.Equal(50, sample.OriginalLength);
            Assert.Equal(64, sample.Samples.Length);
            Assert.Equal(16, sample.LoopStart);
            Assert.Equal(48, sample.LoopEnd);
        }
    }
}
