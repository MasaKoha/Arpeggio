using System;
using System.Text;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>NSF の三つの ASCII メタデータ欄を検証・縮約する。</summary>
    internal static class NsfMetadata
    {
        private const int FieldBytes = 32;
        private const int MaximumTextBytes = FieldBytes - 1;
        private const int MaximumAsciiCharacter = 0x7F;
        private const byte ReplacementCharacter = (byte)'?';

        internal static byte[]? Encode(string title, string author, string copyright, ConversionReport report)
        {
            string[] values = { title, author, copyright };
            string[] names = { "title", "author", "copyright" };
            var metadata = new byte[values.Length * FieldBytes];
            var original = new StringBuilder();
            var converted = new StringBuilder();
            int reducedFields = 0;
            for (int fieldIndex = 0; fieldIndex < values.Length; fieldIndex++)
            {
                byte[]? bytes = EncodeField(values[fieldIndex], names[fieldIndex], report, out bool reduced);
                if (bytes is null)
                {
                    continue;
                }
                bytes.CopyTo(metadata, fieldIndex * FieldBytes);
                if (reduced)
                {
                    reducedFields++;
                    original.Append(names[fieldIndex]).Append(": ").Append(values[fieldIndex]).Append('\n');
                    converted.Append(names[fieldIndex]).Append(": ")
                        .Append(Encoding.ASCII.GetString(bytes, 0, Array.IndexOf(bytes, (byte)0))).Append('\n');
                }
            }
            if (reducedFields != 0)
            {
                // 位置を持たない同一コードは共通レポートで集約されるため、縮約した全欄を一明細に残す。
                report.AddWarning(new ConversionDiagnostic("MetadataReduced", "NSF のメタデータを ASCII 31 byte 以下へ縮約しました。")
                {
                    Original = original.ToString(), Converted = converted.ToString(), OccurrenceCount = reducedFields
                });
            }
            return report.ErrorCount == 0 ? metadata : null;
        }

        private static byte[]? EncodeField(string value, string fieldName, ConversionReport report, out bool reduced)
        {
            reduced = false;
            ConversionLimits.ValidateMetadata(value, fieldName, report);
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var bytes = new byte[FieldBytes];
            int outputIndex = 0;
            for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
            {
                char character = value[characterIndex];
                if (!ConsumeScalar(value, ref characterIndex))
                {
                    report.AddError(new ConversionDiagnostic("InvalidMetadata", $"{fieldName} に不正な UTF-16 サロゲートが含まれています。")
                    {
                        Original = fieldName
                    });
                    return null;
                }
                byte converted = character <= MaximumAsciiCharacter ? (byte)character : ReplacementCharacter;
                reduced |= character > MaximumAsciiCharacter || outputIndex >= MaximumTextBytes;
                if (outputIndex < MaximumTextBytes)
                {
                    bytes[outputIndex++] = converted;
                }
            }
            return bytes;
        }

        private static bool ConsumeScalar(string value, ref int characterIndex)
        {
            char character = value[characterIndex];
            if (!char.IsSurrogate(character))
            {
                return true;
            }
            if (!char.IsHighSurrogate(character) || characterIndex + 1 >= value.Length ||
                !char.IsLowSurrogate(value[characterIndex + 1]))
            {
                return false;
            }
            characterIndex++;
            return true;
        }
    }
}
