using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>生成側の型・定数を使わず、今回の NSF v1 ヘッダーから独立 CPU の bank mapping を構築する。</summary>
    internal sealed class IndependentNsfLoader
    {
        private const int HeaderBytes = 128;
        private const int BankBytes = 4096;
        private const int MaximumRomBytes = 1048576;
        private const int FirstAddress = 0x8000;
        private const int LastFixedAddress = 0x8FFF;
        private const int FieldBytes = 32;
        private const int ByteShift = 8;

        private IndependentNsfLoader(byte[] bytes)
        {
            int romBytes = bytes.Length - HeaderBytes;
            Require(romBytes >= 2 * BankBytes && romBytes <= MaximumRomBytes && romBytes % BankBytes == 0,
                "ROM の長さ・4 KiB 整列が不正です。");
            Require(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x4E, 0x45, 0x53, 0x4D, 0x1A, 1, 1, 1 }), "署名・version・曲数が不正です。");
            Require(ReadWord(bytes, 0x08) == FirstAddress, "load アドレスが不正です。");
            InitAddress = ReadWord(bytes, 0x0A);
            PlayAddress = ReadWord(bytes, 0x0C);
            Require(InitAddress is >= FirstAddress and <= LastFixedAddress &&
                PlayAddress is >= FirstAddress and <= LastFixedAddress, "入口が固定 bank 外です。");
            Require(ReadWord(bytes, 0x6E) == 16639 && ReadWord(bytes, 0x78) == 19997, "速度が不正です。");
            byte[] banks = bytes.AsSpan(0x70, 8).ToArray();
            Require(banks.AsSpan().SequenceEqual(new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 }), "初期 bank が不正です。");
            Require(bytes.Skip(0x7A).Take(6).All(value => value == 0), "region・拡張音源・予約 byte が不正です。");
            Title = ReadField(bytes, 0x0E);
            Author = ReadField(bytes, 0x2E);
            Copyright = ReadField(bytes, 0x4E);
            RomBytes = romBytes;
            Memory = new Limited6502Memory();
            Memory.MapRom(bytes.AsSpan(HeaderBytes).ToArray(), banks);
        }

        internal ushort InitAddress { get; }
        internal ushort PlayAddress { get; }
        internal string Title { get; }
        internal string Author { get; }
        internal string Copyright { get; }
        internal int RomBytes { get; }
        internal Limited6502Memory Memory { get; }

        internal static IndependentNsfLoader Load(string path) => Load(File.ReadAllBytes(path));

        internal static IndependentNsfLoader Load(byte[] bytes) => new IndependentNsfLoader(bytes);

        private static ushort ReadWord(byte[] bytes, int offset)
            => (ushort)(bytes[offset] | bytes[offset + 1] << ByteShift);

        private static string ReadField(byte[] bytes, int offset)
        {
            int length = 0;
            while (length < FieldBytes && bytes[offset + length] != 0)
            {
                Require(bytes[offset + length] <= 0x7F, "ASCII 以外のメタデータです。");
                length++;
            }
            Require(length < FieldBytes, "メタデータが NUL 終端していません。");
            Require(bytes.Skip(offset + length).Take(FieldBytes - length).All(value => value == 0), "NUL 後のメタデータがゼロ埋めされていません。");
            return Encoding.ASCII.GetString(bytes, offset, length);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
