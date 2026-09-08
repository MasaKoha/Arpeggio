using System;
using System.IO;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>writer を呼ばない手書き VGM で、独立パーサー自身の受理・破損検出を検証する。</summary>
    public sealed class IndependentVgmParserTests
    {
        private const int FixtureBytes = 300;
        private const int DataStart = 256;
        private const int Gd3Start = 266;
        private const int HeaderSamplesField = 0x18;
        private const int DataOffsetField = 0x34;
        private const int NesClockField = 0x84;
        private const int WaitLowPosition = 260;
        private const int WaitHighPosition = 261;
        private const int Gd3LengthField = 274;
        private const int MaximumWait = 65535;

        /// <summary>固定 byte 列の二つの書き込みと 1 サンプル待機・11 空文字列を読み戻す。</summary>
        [Fact]
        public void HandWrittenFixtureDecodesKnownTrace()
        {
            ParsedVgm parsed = IndependentVgmParser.Parse(CreateFixture());
            Assert.Equal(FixtureBytes, parsed.FileBytes);
            Assert.Equal(DataStart, parsed.DataStart);
            Assert.Equal(Gd3Start, parsed.Gd3Start);
            Assert.Equal(new[] { (0L, 0x4015, 0, 0), (1L, 0x4015, 0, 1) }, parsed.Writes);
            Assert.Equal(new[] { 1 }, parsed.Waits);
            Assert.Equal(1L, parsed.WaitSamples);
            Assert.Equal(22U, parsed.Gd3PayloadBytes);
            Assert.Equal(11, parsed.Gd3Fields.Count);
            Assert.All(parsed.Gd3Fields, field => Assert.Equal(string.Empty, field));
        }

        /// <summary>待機の上位 byte を符号付きとして誤読しない。</summary>
        [Fact]
        public void WaitOperandUsesBothUnsignedBytes()
        {
            byte[] bytes = CreateFixture();
            bytes[HeaderSamplesField] = 0xFF;
            bytes[HeaderSamplesField + 1] = 0xFF;
            bytes[WaitLowPosition] = 0xFF;
            bytes[WaitHighPosition] = 0xFF;
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(new[] { MaximumWait }, parsed.Waits);
            Assert.Equal(MaximumWait, parsed.Writes[1].Sample);
        }

        /// <summary>全相対位置・予約欄・命令・タグの壊れ方を個別に検出する。</summary>
        [Theory]
        [InlineData(0x00, 0)]
        [InlineData(0x04, 0)]
        [InlineData(0x08, 0xAA)]
        [InlineData(0x14, 0)]
        [InlineData(0x18, 2)]
        [InlineData(0x1C, 1)]
        [InlineData(0x20, 1)]
        [InlineData(0x24, 60)]
        [InlineData(0x34, 0)]
        [InlineData(0x80, 1)]
        [InlineData(0x84, 0)]
        [InlineData(0x87, 0x80)]
        [InlineData(0xF8, 1)]
        [InlineData(256, 0xB3)]
        [InlineData(256, 0x67)]
        [InlineData(257, 0x95)]
        [InlineData(259, 0x62)]
        [InlineData(260, 0)]
        [InlineData(265, 0)]
        [InlineData(266, 0)]
        [InlineData(270, 1)]
        [InlineData(274, 21)]
        [InlineData(274, 24)]
        [InlineData(278, 1)]
        [InlineData(299, 1)]
        public void CorruptedFieldIsRejected(int position, byte value)
        {
            byte[] bytes = CreateFixture();
            bytes[position] = value;
            Assert.Throws<InvalidDataException>(() => IndependentVgmParser.Parse(bytes));
        }

        /// <summary>全 byte 境界の切断を拒否し、EOF だけ修正したタグ切断も拒否する。</summary>
        [Fact]
        public void EveryTruncationAndTrailingByteAreRejected()
        {
            byte[] fixture = CreateFixture();
            for (int length = 0; length < fixture.Length; length++)
            {
                var truncated = new byte[length];
                Array.Copy(fixture, truncated, length);
                Assert.Throws<InvalidDataException>(() => IndependentVgmParser.Parse(truncated));
            }
            var missingTerminator = new byte[FixtureBytes - 2];
            Array.Copy(fixture, missingTerminator, missingTerminator.Length);
            missingTerminator[0x04] -= 2;
            missingTerminator[Gd3LengthField] -= 2;
            Assert.Throws<InvalidDataException>(() => IndependentVgmParser.Parse(missingTerminator));
            var trailingByte = new byte[FixtureBytes + 1];
            Array.Copy(fixture, trailingByte, fixture.Length);
            trailingByte[0x04]++;
            Assert.Throws<InvalidDataException>(() => IndependentVgmParser.Parse(trailingByte));
        }

        private static byte[] CreateFixture()
        {
            var bytes = new byte[FixtureBytes];
            Array.Copy(new byte[] { 0x56, 0x67, 0x6D, 0x20, 0x28, 0x01, 0, 0, 0x71, 0x01, 0, 0 }, bytes, 12);
            bytes[0x14] = 0xF6;
            bytes[HeaderSamplesField] = 1;
            bytes[DataOffsetField] = 0xCC;
            Array.Copy(new byte[] { 0x4D, 0x4F, 0x1B, 0 }, 0, bytes, NesClockField, 4);
            Array.Copy(new byte[] { 0xB4, 0x15, 0, 0x61, 1, 0, 0xB4, 0x15, 0, 0x66 }, 0, bytes, DataStart, 10);
            Array.Copy(new byte[] { 0x47, 0x64, 0x33, 0x20, 0, 1, 0, 0, 22, 0, 0, 0 }, 0, bytes, Gd3Start, 12);
            return bytes;
        }
    }
}
