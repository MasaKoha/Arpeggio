using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Arpeggio.Formats.Export;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>NSF の量子化済み列をテスト内で VGM 化し、実装の内部コンストラクターに依存しない。</summary>
    internal static class QuantizedNesVgmFixture
    {
        private const int HeaderBytes = 256;
        private const int EndOfFileField = 0x04;
        private const int VersionField = 0x08;
        private const int Gd3Field = 0x14;
        private const int SamplesField = 0x18;
        private const int DataField = 0x34;
        private const int ClockField = 0x84;
        private const int EmptyTagBytes = 22;
        private const int RegisterBase = 0x4000;

        internal static long SampleAtFrame(long frame)
        {
            const long playMicroseconds = 16639;
            const long microsecondsPerSecond = 1000000;
            return (frame * playMicroseconds * RegisterTraceRenderer.SampleRate + microsecondsPerSecond / 2) / microsecondsPerSecond;
        }

        internal static byte[] Write(NsfFrameTimeline timeline)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(new byte[HeaderBytes]);
            long currentSample = 0;
            foreach (NsfRegisterWrite write in timeline.Writes)
            {
                WriteWait(writer, SampleAtFrame(write.Frame) - currentSample);
                currentSample = SampleAtFrame(write.Frame);
                writer.Write((byte)0xB4);
                writer.Write(checked((byte)(write.Address - RegisterBase)));
                writer.Write(write.Value);
            }
            long endSample = SampleAtFrame(timeline.EndFrame);
            WriteWait(writer, endSample - currentSample);
            writer.Write((byte)0x66);
            int tagStart = checked((int)stream.Position);
            writer.Write(new byte[] { (byte)'G', (byte)'d', (byte)'3', (byte)' ' });
            writer.Write(0x100U);
            writer.Write(EmptyTagBytes);
            writer.Write(new byte[EmptyTagBytes]);
            writer.Flush();
            byte[] bytes = stream.ToArray();
            bytes[0] = (byte)'V'; bytes[1] = (byte)'g'; bytes[2] = (byte)'m'; bytes[3] = (byte)' ';
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(EndOfFileField), (uint)(bytes.Length - EndOfFileField));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(VersionField), 0x171);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Gd3Field), (uint)(tagStart - Gd3Field));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(SamplesField), checked((uint)endSample));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(DataField), HeaderBytes - DataField);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ClockField), 1789773);
            return bytes;
        }

        private static void WriteWait(BinaryWriter writer, long samples)
        {
            while (samples > 0)
            {
                ushort wait = (ushort)Math.Min(samples, ushort.MaxValue);
                writer.Write((byte)0x61);
                writer.Write(wait);
                samples -= wait;
            }
        }
    }
}
