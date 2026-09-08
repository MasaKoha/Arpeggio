using System;
using System.IO;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>壊れた長さや非対応 SMF を部分成功にしないことを検証する。</summary>
    public sealed class MidiReaderInvalidInputTests
    {
        /// <summary>format・division・track count の不正組み合わせを拒否する。</summary>
        [Theory]
        [InlineData(2, 480, 1)]
        [InlineData(3, 480, 1)]
        [InlineData(0, 0, 1)]
        [InlineData(1, 0xE728, 1)]
        [InlineData(0, 480, 0)]
        [InlineData(0, 480, 2)]
        [InlineData(1, 480, 0)]
        [InlineData(1, 480, 257)]
        public void InvalidHeaderIsRejected(int format, int division, int trackCount)
        {
            var tracks = new byte[trackCount][];
            Array.Fill(tracks, MidiFileFixture.Bytes("00 FF 2F 00"));
            MidiFileFixture.Reject(MidiFileFixture.Create(format, division, tracks));
        }

        /// <summary>RMID・UMP 等の非 SMF と短いヘッダー・巨大宣言を拒否する。</summary>
        [Theory]
        [InlineData("52494646 00000000 524D4944")]
        [InlineData("554D5020 00000000")]
        [InlineData("4D546864 00000005 0000000101")]
        [InlineData("4D546864 FFFFFFFF")]
        [InlineData("4D546864 00000006 0000000101E0 4D54726B FFFFFFFF")]
        [InlineData("4D546864 00000006 0000000101E0 4A554E4B FFFFFFFF")]
        [InlineData("4D546864 00000006 0000000101E0 4D54726B 00000005 00FF2F00")]
        public void InvalidContainerIsRejected(string bytes)
        {
            MidiFileFixture.Reject(MidiFileFixture.Bytes(bytes));
        }

        /// <summary>VLQ・running status・system status・データ MSB・EOT・meta の不正を拒否する。</summary>
        [Theory]
        [InlineData("00 3C 40 00 FF 2F 00")]
        [InlineData("8180808000 90 3C 40 00 FF 2F 00")]
        [InlineData("80808080")]
        [InlineData("81")]
        [InlineData("00 90 80 40 00 FF 2F 00")]
        [InlineData("00 90 3C 80 00 FF 2F 00")]
        [InlineData("00 C0 80 00 FF 2F 00")]
        [InlineData("00 D0 FF 00 FF 2F 00")]
        [InlineData("00 90 3C 40")]
        [InlineData("00 FF 2F 00 00")]
        [InlineData("00 FF 2F 01 00")]
        [InlineData("00 FF 51 03 000000 00 FF 2F 00")]
        [InlineData("00 FF 51 02 0100 00 FF 2F 00")]
        [InlineData("00 FF 51 04 01000000 00 FF 2F 00")]
        [InlineData("00 FF 21 01 01 00 FF 2F 00")]
        [InlineData("00 FF 21 00 00 FF 2F 00")]
        [InlineData("00 FF 00 01 00 00 FF 2F 00")]
        [InlineData("00 FF 20 02 0000 00 FF 2F 00")]
        [InlineData("00 FF 54 04 00000000 00 FF 2F 00")]
        [InlineData("00 FF 58 03 040218 00 FF 2F 00")]
        [InlineData("00 FF 59 03 000000 00 FF 2F 00")]
        [InlineData("00 FF 03 7F 41 00 FF 2F 00")]
        [InlineData("00 FF 7F FFFFFF7F 00 FF 2F 00")]
        [InlineData("00 FF 01 8080808000 00 FF 2F 00")]
        [InlineData("00 F0 7F 00 FF 2F 00")]
        [InlineData("00 F7 8180808000 00 FF 2F 00")]
        [InlineData("00 90 3C 40 00 FF 01 00 00 3C 00 00 FF 2F 00")]
        [InlineData("00 90 3C 40 00 F0 00 00 3C 00 00 FF 2F 00")]
        [InlineData("00 90 3C 40 00 F7 00 00 3C 00 00 FF 2F 00")]
        public void InvalidTrackIsRejected(string track)
        {
            MidiFileFixture.Reject(MidiFileFixture.Create(MidiFileFixture.Bytes(track)));
        }

        /// <summary>SMF に採用しない system status は長さを推測せず拒否する。</summary>
        [Theory]
        [InlineData(0xF1)]
        [InlineData(0xF2)]
        [InlineData(0xF3)]
        [InlineData(0xF4)]
        [InlineData(0xF5)]
        [InlineData(0xF6)]
        [InlineData(0xF8)]
        [InlineData(0xF9)]
        [InlineData(0xFA)]
        [InlineData(0xFB)]
        [InlineData(0xFC)]
        [InlineData(0xFD)]
        [InlineData(0xFE)]
        public void UnsupportedSystemStatusIsRejected(byte status)
        {
            ConversionReport report = MidiFileFixture.Reject(MidiFileFixture.Create(new byte[] { 0, status, 0, 0xFF, 0x2F, 0 }));
            Assert.Contains(report.Errors, diagnostic => diagnostic.Code == "UnsupportedMidiStatus");
        }

        /// <summary>running status は次の MTrk へ持ち越さない。</summary>
        [Fact]
        public void RunningStatusDoesNotCrossTracks()
        {
            byte[] first = MidiFileFixture.Bytes("00 90 3C 40 00 FF 2F 00");
            byte[] second = MidiFileFixture.Bytes("00 3C 00 00 FF 2F 00");
            MidiFileFixture.Reject(MidiFileFixture.Create(first, second));
        }

        /// <summary>meta / SysEx の後でも status を明示すれば受理する。</summary>
        [Theory]
        [InlineData("FF 01 00")]
        [InlineData("F0 00")]
        [InlineData("F7 00")]
        public void ExplicitStatusAfterSystemMessageIsAccepted(string message)
        {
            byte[] track = MidiFileFixture.Bytes($"00 90 3C 40 00 {message} 01 80 3C 00 00 FF 2F 00");
            MidiFileFixture.Read(MidiFileFixture.Create(track), MidiFileFixture.Report());
        }

        /// <summary>必要な全 byte 境界で切断した入力を拒否する。</summary>
        [Theory]
        [InlineData("00 90 3C 40 8360 3D 40 00 FF 51 03 07A120 00 F0 03 017FF7 00 FF 2F 00")]
        [InlineData("00 FF 03 03 E69BB2 00 FF 2F 00")]
        public void EveryTruncationBoundaryIsRejected(string track)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(track), MidiFileFixture.Bytes("00 FF 2F 00"));
            for (int length = 0; length < bytes.Length; length++)
            {
                MidiFileFixture.Reject(bytes.AsSpan(0, length).ToArray());
            }
            MidiFileFixture.Read(bytes, MidiFileFixture.Report());
        }

        /// <summary>MTrk の過不足、重複 MThd、末尾の途中チャンクを拒否する。</summary>
        [Theory]
        [InlineData("4D54726B 00000004 00FF2F00")]
        [InlineData("4D546864 00000006 0000000101E0")]
        [InlineData("4A55")]
        [InlineData("4A554E4B 00000003 0000")]
        public void InvalidTrailingChunkIsRejected(string trailing)
        {
            using var stream = new MemoryStream();
            stream.Write(MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00")));
            stream.Write(MidiFileFixture.Bytes(trailing));
            MidiFileFixture.Reject(stream.ToArray());
        }

        /// <summary>ヘッダーが宣言した二つ目の MTrk がなければ拒否する。</summary>
        [Fact]
        public void MissingDeclaredTrackIsRejected()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00"));
            bytes[11] = 2;
            MidiFileFixture.Reject(bytes);
        }

        /// <summary>先行エラーがあれば Stream を消費しない。</summary>
        [Fact]
        public void ExistingErrorsPreventReading()
        {
            using var stream = new MemoryStream(MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00")));
            ConversionReport report = MidiFileFixture.Report();
            report.AddError(new ConversionDiagnostic("ExistingError", "先行失敗。"));
            Assert.Null(MidiReader.Read(stream, report));
            Assert.Equal(0L, stream.Position);
        }
    }
}
