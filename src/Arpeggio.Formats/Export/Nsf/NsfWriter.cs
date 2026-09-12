using System;
using System.Buffers.Binary;
using System.IO;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>検証済みデータと自作プレイヤーを NSF v1 のヘッダー・固定 bank・データ bank として保存する。</summary>
    public static class NsfWriter
    {
        private const int SignatureOffset = 0;
        private const uint Signature = 0x4D53454E;
        private const int SignatureTerminatorOffset = 4;
        private const byte SignatureTerminator = 0x1A;
        private const int VersionOffset = 5;
        private const byte Version = 1;
        private const int TotalSongsOffset = 6;
        private const int StartingSongOffset = 7;
        private const byte SingleSong = 1;
        private const int LoadAddressOffset = 0x08;
        private const int InitAddressOffset = 0x0A;
        private const int PlayAddressOffset = 0x0C;
        private const int TitleOffset = 0x0E;
        private const int NtscSpeedOffset = 0x6E;
        private const int FirstDataBankOffset = 0x71;
        private const int PalSpeedOffset = 0x78;
        private const ushort PalMicroseconds = 19997;
        private const ushort LoadAddress = 0x8000;
        private const byte FirstDataBank = 1;

        /// <summary>CPU 予算・メタデータを検証後に現在位置へ NSF を書く。拒否時は false で無変更。Seek 不要で Stream は閉じず、I/O 例外と部分書き込みは呼び出し元へ委ねる。</summary>
        public static bool Write(Stream destination, NsfEncodedData data, string title, ConversionReport report,
            string author = "", string copyright = "")
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(report);
            if (!destination.CanWrite)
            {
                throw new ArgumentException("書き込み可能な Stream を指定してください。", nameof(destination));
            }
            Action<Stream>? write = Prepare(data, title, report, author, copyright);
            if (write is null)
            {
                return false;
            }
            write(destination);
            return true;
        }

        internal static Action<Stream>? Prepare(NsfEncodedData data, string title, ConversionReport report,
            string author, string copyright)
        {
            NsfDriverImage? image = NsfDriverBuilder.Build(data, report);
            byte[]? metadata = NsfMetadata.Encode(title, author, copyright, report);
            report.SetOutputMetrics(data.EndFrame * NsfTiming.PlayMicroseconds /
                (double)NsfTiming.MicrosecondsPerSecond, data.OutputBytes);
            if (image is null || metadata is null || !report.CanWrite)
            {
                return null;
            }
            byte[] header = CreateHeader(image);
            metadata.CopyTo(header, TitleOffset);
            return destination =>
            {
                destination.Write(header);
                WriteBanks(destination, image, data);
            };
        }

        private static byte[] CreateHeader(NsfDriverImage image)
        {
            var header = new byte[ConversionLimits.NsfHeaderBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(SignatureOffset), Signature);
            header[SignatureTerminatorOffset] = SignatureTerminator;
            header[VersionOffset] = Version;
            header[TotalSongsOffset] = SingleSong;
            header[StartingSongOffset] = SingleSong;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(LoadAddressOffset), LoadAddress);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(InitAddressOffset), image.InitAddress);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PlayAddressOffset), image.PlayAddress);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(NtscSpeedOffset), NsfTiming.PlayMicroseconds);
            header[FirstDataBankOffset] = FirstDataBank;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PalSpeedOffset), PalMicroseconds);
            return header;
        }

        private static void WriteBanks(Stream destination, NsfDriverImage image, NsfEncodedData data)
        {
            var bank = new byte[ConversionLimits.NsfPlayerBytes];
            for (int index = 0; index < bank.Length; index++)
            {
                bank[index] = image.Bank[index];
            }
            destination.Write(bank);
            int dataOffset = 0;
            while (dataOffset < data.Bytes.Count)
            {
                Array.Clear(bank);
                int count = Math.Min(bank.Length, data.Bytes.Count - dataOffset);
                for (int index = 0; index < count; index++)
                {
                    bank[index] = data.Bytes[dataOffset + index];
                }
                destination.Write(bank);
                dataOffset += count;
            }
        }
    }
}
