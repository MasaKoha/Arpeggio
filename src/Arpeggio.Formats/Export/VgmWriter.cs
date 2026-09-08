using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Export
{
    /// <summary>不変の NES／GB レジスタ列を VGM v1.71 と日本語対応 GD3 へ書き出す。</summary>
    public static class VgmWriter
    {
        private const int HeaderBytes = 0x100;
        private const uint Signature = 0x206D6756;
        private const uint Version = 0x00000171;
        private const int EndOfFileOffset = 0x04;
        private const int VersionOffset = 0x08;
        private const int Gd3Offset = 0x14;
        private const int TotalSamplesOffset = 0x18;
        private const int DataOffset = 0x34;
        private const int GameBoyClockOffset = 0x80;
        private const int NesClockOffset = 0x84;
        private const uint NesClockRate = 1789773;
        private const uint GameBoyClockRate = 4194304;
        private const int NesAddressBase = 0x4000;
        private const int GameBoyAddressBase = 0xFF10;
        private const byte NesWriteCommand = 0xB4;
        private const byte GameBoyWriteCommand = 0xB3;
        private const byte WaitCommand = 0x61;
        private const byte EndCommand = 0x66;
        private const int RegisterCommandBytes = 3;
        private const int WaitCommandBytes = 3;
        private const int EndCommandBytes = 1;
        private const int MaximumWaitSamples = ushort.MaxValue;

        /// <summary>書き込みを行わず全サイズとメタデータを検証し、予定バイト数をレポートへ設定する。エラーまたは strict 警告時は null。</summary>
        public static long? CalculateSize(RegisterTimeline timeline, string title, string author, ConversionReport report)
        {
            Gd3Tag? tag = Prepare(timeline, title, author, report);
            return tag is null ? null : report.OutputBytes;
        }

        /// <summary>全検証後、現在位置から完全な VGM を書く。拒否時は false で Stream を変更しない。Seek は不要で Stream は閉じない。I/O 例外は伝播し、部分書き込みは巻き戻さない。</summary>
        public static bool Write(Stream destination, RegisterTimeline timeline, string title, string author, ConversionReport report)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("書き込み可能な Stream を指定してください。", nameof(destination));
            }
            Action<Stream>? write = PrepareWrite(timeline, title, author, report);
            if (write is null)
            {
                return false;
            }
            write(destination);
            return true;
        }

        internal static Action<Stream>? PrepareWrite(RegisterTimeline timeline, string title, string author, ConversionReport report)
        {
            Gd3Tag? tag = Prepare(timeline, title, author, report);
            if (tag is null)
            {
                return null;
            }
            byte[] header = CreateHeader(timeline, report.OutputBytes, tag.Length);
            return destination =>
            {
                using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
                writer.Write(header);
                WriteCommands(writer, timeline);
                tag.Write(writer);
            };
        }

        private static Gd3Tag? Prepare(RegisterTimeline timeline, string title, string author, ConversionReport report)
        {
            if (timeline is null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }
            if (timeline.Chip != report.Chip)
            {
                throw new ArgumentException("レジスタ列とレポートのチップが一致していません。", nameof(report));
            }
            if (report.Format != ConversionFormat.Vgm)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedFormat", "VGM writer には VGM のレポートを指定してください。"));
            }
            if (timeline.Chip != ChipKind.Nes && timeline.Chip != ChipKind.GameBoy)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "VGM は NES または Game Boy のレジスタ列だけを受け付けます。"));
            }
            Gd3Tag? tag = Gd3Tag.Create(timeline.Chip, title, author, report);
            if (tag is null)
            {
                return null;
            }
            long outputBytes = checked(HeaderBytes + CalculateCommandBytes(timeline) + tag.Length);
            report.SetOutputMetrics(timeline.EndSamples / (double)ConversionLimits.ControlSampleRate, outputBytes);
            ConversionLimits.ValidateExportSize(report, timeline.Writes.Count, outputBytes);
            return report.CanWrite ? tag : null;
        }

        private static long CalculateCommandBytes(RegisterTimeline timeline)
        {
            long waitCommands = 0;
            long positionSamples = 0;
            foreach (RegisterWrite write in timeline.Writes)
            {
                waitCommands += CountWaitCommands(write.PositionSamples - positionSamples);
                positionSamples = write.PositionSamples;
            }
            waitCommands += CountWaitCommands(timeline.EndSamples - positionSamples);
            return checked((long)timeline.Writes.Count * RegisterCommandBytes + waitCommands * WaitCommandBytes + EndCommandBytes);
        }

        private static long CountWaitCommands(long samples)
        {
            return samples / MaximumWaitSamples + (samples % MaximumWaitSamples == 0 ? 0 : 1);
        }

        private static byte[] CreateHeader(RegisterTimeline timeline, long outputBytes, int tagBytes)
        {
            var header = new byte[HeaderBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(header, Signature);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(EndOfFileOffset), checked((uint)(outputBytes - EndOfFileOffset)));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(VersionOffset), Version);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(Gd3Offset), checked((uint)(outputBytes - tagBytes - Gd3Offset)));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(TotalSamplesOffset), checked((uint)timeline.EndSamples));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(DataOffset), HeaderBytes - DataOffset);
            int clockOffset = timeline.Chip == ChipKind.Nes ? NesClockOffset : GameBoyClockOffset;
            uint clockRate = timeline.Chip == ChipKind.Nes ? NesClockRate : GameBoyClockRate;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(clockOffset), clockRate);
            return header;
        }

        private static void WriteCommands(BinaryWriter writer, RegisterTimeline timeline)
        {
            byte command = timeline.Chip == ChipKind.Nes ? NesWriteCommand : GameBoyWriteCommand;
            int addressBase = timeline.Chip == ChipKind.Nes ? NesAddressBase : GameBoyAddressBase;
            long positionSamples = 0;
            foreach (RegisterWrite write in timeline.Writes)
            {
                WriteWait(writer, write.PositionSamples - positionSamples);
                writer.Write(command);
                writer.Write(checked((byte)(write.Address - addressBase)));
                writer.Write(write.Value);
                positionSamples = write.PositionSamples;
            }
            WriteWait(writer, timeline.EndSamples - positionSamples);
            writer.Write(EndCommand);
        }

        private static void WriteWait(BinaryWriter writer, long samples)
        {
            while (samples > 0)
            {
                ushort waitSamples = (ushort)Math.Min(samples, MaximumWaitSamples);
                writer.Write(WaitCommand);
                writer.Write(waitSamples);
                samples -= waitSamples;
            }
        }
    }
}
