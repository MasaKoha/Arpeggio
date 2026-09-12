using System;
using System.IO;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>writer を使わない手書きファイルで独立ローダー自身のヘッダー・ROM 検証を確かめる。</summary>
    public sealed class IndependentNsfLoaderTests
    {
        /// <summary>固定ヘッダーから二つの RTS と bank 1 をロードし、未マップ bank を拒否する。</summary>
        [Fact]
        public void HandwrittenFileLoadsEntryPointsAndInitialMapping()
        {
            IndependentNsfLoader file = IndependentNsfLoader.Load(CreateFile());
            Assert.Equal(0x8000, file.InitAddress);
            Assert.Equal(0x8001, file.PlayAddress);
            Assert.Equal(8192, file.RomBytes);
            Assert.Equal("T", file.Title);
            Assert.Equal(string.Empty, file.Author);
            Assert.Equal(string.Empty, file.Copyright);
            var processor = new Limited6502(file.Memory);
            Assert.Equal(6L, processor.Call(file.InitAddress, 6));
            Assert.Equal(6L, processor.Call(file.PlayAddress, 6));
            Assert.Equal((byte)2, file.Memory.Read(0x9000));
            Assert.Equal((byte)0x60, file.Memory.Read(0xA000));
            file.Memory.Write(0x5FF9, 0, 0);
            Assert.Equal((byte)0x60, file.Memory.Read(0x9000));
            Assert.Throws<InvalidOperationException>(() => file.Memory.Write(0x8000, 0, 0));
            Assert.Throws<InvalidOperationException>(() => file.Memory.Write(0x5FF8, 1, 0));
            file.Memory.Write(0x5FF9, 2, 0);
            Assert.Throws<InvalidOperationException>(() => file.Memory.Read(0x9000));
        }

        /// <summary>ヘッダーの署名・速度・入口・バンク・予約 byte・ASCII を個別に破壊して拒否する。</summary>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(4, 0)]
        [InlineData(5, 2)]
        [InlineData(6, 2)]
        [InlineData(7, 0)]
        [InlineData(8, 1)]
        [InlineData(0x0B, 0x90)]
        [InlineData(0x0D, 0x7F)]
        [InlineData(0x6E, 0)]
        [InlineData(0x70, 1)]
        [InlineData(0x71, 0)]
        [InlineData(0x77, 1)]
        [InlineData(0x78, 0)]
        [InlineData(0x7A, 1)]
        [InlineData(0x7B, 1)]
        [InlineData(0x7F, 1)]
        [InlineData(0x0E, 0x80)]
        [InlineData(0x10, 1)]
        public void CorruptedHeaderIsRejected(int offset, byte value)
        {
            byte[] bytes = CreateFile();
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => IndependentNsfLoader.Load(bytes));
        }

        /// <summary>全 byte 境界の切断・余剰 byte・最大容量超過・NUL 欠落を拒否する。</summary>
        [Fact]
        public void TruncationAlignmentCapacityAndStringTerminationAreChecked()
        {
            byte[] bytes = CreateFile();
            for (int length = 0; length < bytes.Length; length++)
            {
                byte[] truncated = bytes.AsSpan(0, length).ToArray();
                Assert.Throws<InvalidDataException>(() => IndependentNsfLoader.Load(truncated));
            }
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.Throws<InvalidDataException>(() => IndependentNsfLoader.Load(bytes));
            bytes = new byte[128 + 1048576 + 4096];
            Assert.Throws<InvalidDataException>(() => IndependentNsfLoader.Load(bytes));
            bytes = CreateFile();
            Array.Fill(bytes, (byte)'A', 0x0E, 32);
            Assert.Throws<InvalidDataException>(() => IndependentNsfLoader.Load(bytes));
        }

        private static byte[] CreateFile()
        {
            var bytes = new byte[128 + 8192];
            new byte[] { 0x4E, 0x45, 0x53, 0x4D, 0x1A, 1, 1, 1, 0, 0x80, 0, 0x80, 1, 0x80, 0x54 }.CopyTo(bytes, 0);
            bytes[0x6E] = 0xFF;
            bytes[0x6F] = 0x40;
            bytes[0x71] = 1;
            bytes[0x78] = 0x1D;
            bytes[0x79] = 0x4E;
            bytes[128] = 0x60;
            bytes[129] = 0x60;
            bytes[128 + 4096] = 2;
            return bytes;
        }
    }
}
