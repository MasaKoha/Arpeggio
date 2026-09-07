using System;
using Arpeggio.Core.Instruments;
using Xunit;

namespace Arpeggio.Core.Tests.Instruments
{
    /// <summary>保存バイト順・PCM 全範囲の変換・入力上限を検証する。</summary>
    public sealed class SampleDataCodecTests
    {
        /// <summary>既知の little-endian バイト列を生成して復元する。</summary>
        [Fact]
        public void Encode_UsesLittleEndianPcm()
        {
            short[] samples = { short.MinValue, 0, short.MaxValue };
            string encoded = SampleDataCodec.Encode(samples);
            Assert.Equal(new byte[] { 0, 128, 0, 0, 255, 127 }, Convert.FromBase64String(encoded));
            Assert.Equal(samples, SampleDataCodec.Decode(encoded));
        }

        /// <summary>全 65536 値を float 経由で損失なく復元し、両端を ±1 とする。</summary>
        [Fact]
        public void FloatConversion_RoundTripsEveryPcmValue()
        {
            const int ValueCount = 65536;
            short[] samples = new short[ValueCount];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (short)(short.MinValue + index);
            }
            float[] converted = SampleDataCodec.ToFloat(samples);
            Assert.Equal(-1, converted[0]);
            Assert.Equal(1, converted[converted.Length - 1]);
            Assert.Equal(samples, SampleDataCodec.FromFloat(converted));
            Assert.Equal(new short[] { short.MinValue, short.MaxValue }, SampleDataCodec.FromFloat(new float[] { -2, 2 }));
        }

        /// <summary>空・壊れた Base64・端数 PCM・過大データを拒否する。</summary>
        [Fact]
        public void Decode_RejectsInvalidAndOversizedData()
        {
            Assert.Throws<ArgumentException>(() => SampleDataCodec.Decode(string.Empty));
            Assert.Throws<FormatException>(() => SampleDataCodec.Decode("!!!!"));
            Assert.Throws<ArgumentException>(() => SampleDataCodec.Decode("AA=="));
            string oversized = Convert.ToBase64String(new byte[SampleDataCodec.MaximumByteCount + sizeof(short)]);
            Assert.Throws<ArgumentException>(() => SampleDataCodec.Decode(oversized));
            Assert.Throws<ArgumentException>(() => SampleDataCodec.FromFloat(new float[] { float.NaN }));
            Assert.Throws<ArgumentException>(() => SampleDataCodec.FromFloat(new float[] { float.PositiveInfinity }));
        }
    }
}
