using System.IO;
using System.Text;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Export.Vgm
{
    /// <summary>GD3 v1.00 の固定フィールドと UTF-16LE ペイロードを保持する。</summary>
    internal sealed class Gd3Tag
    {
        private const uint Signature = 0x20336447;
        private const uint Version = 0x00000100;
        private const int HeaderBytes = 12;
        private const int MaximumAsciiCharacter = 0x7F;
        private readonly byte[] _payload;

        private Gd3Tag(byte[] payload)
        {
            _payload = payload;
        }

        internal int Length => HeaderBytes + _payload.Length;

        internal static Gd3Tag? Create(ChipKind chip, string title, string author, ConversionReport report)
        {
            ConversionLimits.ValidateMetadata(title, "title", report);
            ConversionLimits.ValidateMetadata(author, "author", report);
            if (report.ErrorCount != 0)
            {
                return null;
            }
            string system = chip == ChipKind.Nes ? "Nintendo Entertainment System" : "Nintendo Game Boy";
            string[] fields =
            {
                IsAscii(title) ? title : string.Empty, title,
                string.Empty, string.Empty, system, string.Empty,
                string.Empty, author, string.Empty, "Arpeggio", string.Empty
            };
            // 置換フォールバックでは元メタデータの欠損を診断できない。
            var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
            try
            {
                return new Gd3Tag(encoding.GetBytes(string.Join("\0", fields) + "\0"));
            }
            catch (EncoderFallbackException)
            {
                report.AddError(new ConversionDiagnostic("InvalidMetadata", "GD3 の曲名・著作者に不正な UTF-16 サロゲートが含まれています。"));
                return null;
            }
        }

        internal void Write(BinaryWriter writer)
        {
            writer.Write(Signature);
            writer.Write(Version);
            writer.Write((uint)_payload.Length);
            writer.Write(_payload);
        }

        private static bool IsAscii(string value)
        {
            foreach (char character in value)
            {
                if (character > MaximumAsciiCharacter)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
