using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Midi
{
    /// <summary>本番の定数・エンコーダーに依存しない SMF のテスト入力。</summary>
    internal static class MidiFileFixture
    {
        internal static byte[] Bytes(string hexadecimal) => Convert.FromHexString(hexadecimal.Replace(" ", string.Empty));

        internal static byte[] Create(params byte[][] tracks) => Create(1, 480, tracks);

        internal static byte[] Create(int format, int division, params byte[][] tracks)
        {
            using var stream = new MemoryStream();
            stream.Write(Bytes("4D546864 00000006"));
            stream.WriteByte((byte)(format >> 8));
            stream.WriteByte((byte)format);
            stream.WriteByte((byte)(tracks.Length >> 8));
            stream.WriteByte((byte)tracks.Length);
            stream.WriteByte((byte)(division >> 8));
            stream.WriteByte((byte)division);
            foreach (byte[] track in tracks)
            {
                stream.Write(Chunk("4D54726B", track));
            }
            return stream.ToArray();
        }

        internal static byte[] Chunk(string identifier, byte[] payload)
        {
            using var stream = new MemoryStream();
            stream.Write(Bytes(identifier));
            stream.WriteByte((byte)(payload.Length >> 24));
            stream.WriteByte((byte)(payload.Length >> 16));
            stream.WriteByte((byte)(payload.Length >> 8));
            stream.WriteByte((byte)payload.Length);
            stream.Write(payload);
            return stream.ToArray();
        }

        internal static ConversionReport Report(bool strict = false) => new ConversionReport(ConversionFormat.Midi, ChipKind.Nes, strict);

        internal static MidiFile Read(byte[] bytes, ConversionReport report)
        {
            using var stream = new MemoryStream(bytes);
            MidiFile? file = MidiReader.Read(stream, report);
            Assert.NotNull(file);
            Assert.Empty(report.Errors);
            Assert.True(stream.CanRead);
            return file;
        }

        internal static ConversionReport Reject(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            ConversionReport report = Report();
            Assert.Null(MidiReader.Read(stream, report));
            Assert.False(report.CanWrite);
            Assert.NotEmpty(report.Errors);
            Assert.True(stream.CanRead);
            return report;
        }
    }
}
