using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>M3 が出力する VGM サブセットを、生成側の定数・型・サイズ計算に依存せず解析する。</summary>
    internal static class IndependentVgmParser
    {
        private const int HeaderBytes = 256;
        private const int IntegerBytes = 4;
        private const int BitsPerByte = 8;
        private const int Gd3HeaderBytes = 12;
        private const int Gd3Fields = 11;
        private const int Utf16CharacterBytes = 2;
        private const int CommandOperandBytes = 2;
        private const int EndOfFileField = 0x04;
        private const int VersionField = 0x08;
        private const int Gd3RelativeField = 0x14;
        private const int TotalSamplesField = 0x18;
        private const int DataRelativeField = 0x34;
        private const int GameBoyClockField = 0x80;
        private const int NesClockField = 0x84;
        private const int Gd3VersionField = 4;
        private const int Gd3LengthField = 8;
        private const int SecondChipAddressFlag = 0x80;
        private const byte WaitCommand = 0x61;
        private const byte EndCommand = 0x66;
        private const byte NesCommand = 0xB4;
        private const byte GameBoyCommand = 0xB3;
        private const int NesBaseAddress = 0x4000;
        private const int GameBoyBaseAddress = 0xFF10;

        internal static ParsedVgm Parse(byte[] bytes)
        {
            Require(bytes.Length >= HeaderBytes + 1 + Gd3HeaderBytes, "ヘッダーが途中で切れています。");
            Require(bytes[0] == 'V' && bytes[1] == 'g' && bytes[2] == 'm' && bytes[3] == ' ', "VGM 署名が不正です。");
            long fileBytes = ReadUnsigned(bytes, EndOfFileField) + (long)EndOfFileField;
            Require(fileBytes == bytes.Length, "EOF 相対位置がファイル長と一致しません。");
            uint version = ReadUnsigned(bytes, VersionField);
            Require(version == 0x171, "VGM の BCD バージョンが不正です。");
            int dataStart = ReadRelativeOffset(bytes, DataRelativeField);
            Require(dataStart == HeaderBytes, "データ開始位置が不正です。");
            int tagStart = ReadRelativeOffset(bytes, Gd3RelativeField);
            uint nesClock = ReadUnsigned(bytes, NesClockField);
            uint gameBoyClock = ReadUnsigned(bytes, GameBoyClockField);
            Require((nesClock == 1789773 && gameBoyClock == 0) || (nesClock == 0 && gameBoyClock == 4194304), "単一チップのクロック欄が不正です。");
            ValidateUnusedHeader(bytes);
            return ReadCommands(bytes, dataStart, tagStart, nesClock, gameBoyClock);
        }

        private static ParsedVgm ReadCommands(byte[] bytes, int dataStart, int tagStart, uint nesClock, uint gameBoyClock)
        {
            var writes = new List<(long Sample, int Address, int Value, int Order)>();
            var waits = new List<int>();
            long sample = 0;
            int position = dataStart;
            bool ended = false;
            while (position < tagStart)
            {
                byte command = bytes[position++];
                if (command == EndCommand)
                {
                    ended = true;
                    break;
                }
                Require(position + CommandOperandBytes <= tagStart, "命令のオペランドが不足しています。");
                if (command == WaitCommand)
                {
                    int wait = bytes[position] | (bytes[position + 1] << BitsPerByte);
                    Require(wait > 0, "ゼロ待機は M3 の出力ではありません。");
                    waits.Add(wait);
                    sample += wait;
                }
                else
                {
                    byte expectedCommand = nesClock != 0 ? NesCommand : GameBoyCommand;
                    Require(command == expectedCommand, "未対応または異なるチップの命令です。");
                    int addressBase = nesClock != 0 ? NesBaseAddress : GameBoyBaseAddress;
                    Require(bytes[position] < SecondChipAddressFlag, "第 2 チップへの書き込みは対象外です。");
                    writes.Add((sample, addressBase + bytes[position], bytes[position + 1], writes.Count));
                }
                position += CommandOperandBytes;
            }
            Require(ended && position == tagStart, "END の直後に GD3 がありません。");
            uint headerSamples = ReadUnsigned(bytes, TotalSamplesField);
            Require(sample == headerSamples, "ヘッダーの総サンプル数と待機の独立総和が一致しません。");
            string[] fields = ReadGd3(bytes, tagStart);
            return new ParsedVgm
            {
                Version = ReadUnsigned(bytes, VersionField), FileBytes = bytes.Length, DataStart = dataStart,
                Gd3Start = tagStart, HeaderSamples = headerSamples, WaitSamples = sample,
                NesClock = nesClock, GameBoyClock = gameBoyClock,
                EndCommandPosition = position - 1, Gd3Version = ReadUnsigned(bytes, tagStart + Gd3VersionField),
                Gd3PayloadBytes = ReadUnsigned(bytes, tagStart + Gd3LengthField),
                Waits = waits.AsReadOnly(), Writes = writes.AsReadOnly(), Gd3Fields = Array.AsReadOnly(fields)
            };
        }

        private static string[] ReadGd3(byte[] bytes, int start)
        {
            Require(start + Gd3HeaderBytes <= bytes.Length, "GD3 ヘッダーが途中で切れています。");
            Require(bytes[start] == 'G' && bytes[start + 1] == 'd' && bytes[start + 2] == '3' && bytes[start + 3] == ' ', "GD3 署名が不正です。");
            Require(ReadUnsigned(bytes, start + Gd3VersionField) == 0x100, "GD3 バージョンが不正です。");
            uint payloadBytes = ReadUnsigned(bytes, start + Gd3LengthField);
            Require(payloadBytes % Utf16CharacterBytes == 0 && start + Gd3HeaderBytes + (long)payloadBytes == bytes.Length,
                "GD3 の宣言長が UTF-16LE の境界またはファイル終端と一致しません。");
            var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
            string payload = encoding.GetString(bytes, start + Gd3HeaderBytes, checked((int)payloadBytes));
            string[] terminatedFields = payload.Split('\0');
            Require(terminatedFields.Length == Gd3Fields + 1 && terminatedFields[Gd3Fields] == string.Empty,
                "GD3 は 11 個の NUL 終端文字列ではありません。");
            var fields = new string[Gd3Fields];
            Array.Copy(terminatedFields, fields, Gd3Fields);
            return fields;
        }

        private static void ValidateUnusedHeader(byte[] bytes)
        {
            int[] usedOffsets = { 0, EndOfFileField, VersionField, Gd3RelativeField, TotalSamplesField,
                DataRelativeField, GameBoyClockField, NesClockField };
            for (int position = 0; position < HeaderBytes; position += IntegerBytes)
            {
                if (Array.IndexOf(usedOffsets, position) < 0)
                {
                    Require(ReadUnsigned(bytes, position) == 0, "ループ・rate・予約・未使用チップ欄に値があります。");
                }
            }
        }

        private static int ReadRelativeOffset(byte[] bytes, int field)
        {
            long position = field + (long)ReadUnsigned(bytes, field);
            Require(position >= HeaderBytes && position < bytes.Length, "相対オフセットがファイル範囲外です。");
            return (int)position;
        }

        private static uint ReadUnsigned(byte[] bytes, int position)
        {
            Require(position >= 0 && position <= bytes.Length - IntegerBytes, "整数が途中で切れています。");
            uint value = 0;
            for (int byteIndex = 0; byteIndex < IntegerBytes; byteIndex++)
            {
                value |= (uint)bytes[position + byteIndex] << (BitsPerByte * byteIndex);
            }
            return value;
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
