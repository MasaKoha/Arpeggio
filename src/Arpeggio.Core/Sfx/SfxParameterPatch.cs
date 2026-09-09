using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>ネストした部分 JSON を全件解析し、完全な候補へ原子的に適用する。</summary>
    public static class SfxParameterPatch
    {
        /// <summary>省略値を保持し、正規化後の同値判定と警告を返す。失敗時も現在値は不変。</summary>
        public static SfxParameterPatchResult Apply(SfxParameters current, ChipKind chip, string json)
        {
            SfxParameters original = SfxParameterValidator.Normalize(current, chip);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw SfxParameterException.Invalid(string.Empty, "空 patch は指定できません。");
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                Dictionary<string, object> changes = new Dictionary<string, object>(StringComparer.Ordinal);
                ReadObject(document.RootElement, string.Empty, chip, changes);
                SfxParameters candidate = ApplyChanges(original, chip, changes);
                return new SfxParameterPatchResult
                {
                    Parameters = candidate,
                    Changed = candidate != original,
                    Warnings = SfxParameterValidator.Validate(candidate, chip)
                };
            }
            catch (JsonException exception)
            {
                throw new SfxParameterException("InvalidParameter", string.Empty,
                    "patch は正しい JSON オブジェクトで指定してください。", exception);
            }
        }

        private static void ReadObject(JsonElement element, string parentPath, ChipKind chip,
            Dictionary<string, object> changes)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw SfxParameterException.Invalid(parentPath, "ネストした JSON オブジェクトを指定してください。null と配列は不許可です。");
            }
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string path = parentPath.Length == 0 ? property.Name : parentPath + "." + property.Name;
                if (!names.Add(property.Name))
                {
                    throw SfxParameterException.Invalid(path, "同じキーを重複して指定できません。");
                }
                ReadProperty(property, path, chip, changes);
            }
            if (names.Count == 0)
            {
                throw SfxParameterException.Invalid(parentPath, "空 patch は指定できません。");
            }
        }

        private static void ReadProperty(JsonProperty property, string path, ChipKind chip,
            Dictionary<string, object> changes)
        {
            // ドット入りのキーを平坦なパスとして受理すると、ネストと重複検出の契約が崩れる。
            if (property.Name.Contains('.'))
            {
                throw SfxParameterException.Unsupported(path);
            }
            if (IsGroup(path, chip))
            {
                ReadObject(property.Value, path, chip, changes);
                return;
            }
            SfxParameterDescription description = SfxParameterCatalog.Get(path, chip);
            object value = ReadValue(property.Value, description);
            changes.Add(path, SfxParameterValidator.NormalizeValue(description, value));
        }

        private static bool IsGroup(string path, ChipKind chip)
        {
            string prefix = path + ".";
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll())
            {
                if (description.IsSupported(chip) && description.Path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static object ReadValue(JsonElement element, SfxParameterDescription description)
        {
            switch (description.ValueKind)
            {
                case SfxParameterValueKind.Boolean:
                    if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                    {
                        return element.GetBoolean();
                    }
                    break;
                case SfxParameterValueKind.Integer:
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double integer)
                        && double.IsFinite(integer) && integer == Math.Truncate(integer)
                        && integer >= int.MinValue && integer <= int.MaxValue && IsIntegerToken(element.GetRawText()))
                    {
                        return (int)integer;
                    }
                    break;
                case SfxParameterValueKind.Number:
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number))
                    {
                        ValidateUnderflow(element, description, number);
                        return number;
                    }
                    break;
                case SfxParameterValueKind.Choice:
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        return element.GetString()!;
                    }
                    break;
            }
            throw SfxParameterException.Invalid(description.Path, "パラメータの JSON 型が仕様と一致しません。null は不許可です。");
        }

        private static void ValidateUnderflow(JsonElement element, SfxParameterDescription description, double number)
        {
            if (number != 0)
            {
                return;
            }
            string token = element.GetRawText();
            if (IsIntegerToken(token))
            {
                return;
            }
            bool isBelowMinimum = description.Minimum is double minimum
                && (minimum > 0 || (minimum == 0 && token[0] == '-'));
            if (isBelowMinimum)
            {
                // 正の微小 repeat が double への変換で0になって無効化されることを防ぐ。
                throw SfxParameterException.Invalid(description.Path, "値は仕様の下限未満です。");
            }
        }

        private static bool IsIntegerToken(string token)
        {
            int exponentIndex = token.IndexOf('e');
            if (exponentIndex < 0)
            {
                exponentIndex = token.IndexOf('E');
            }
            int mantissaEnd = exponentIndex < 0 ? token.Length : exponentIndex;
            int decimalIndex = token.IndexOf('.');
            int fractionDigits = decimalIndex < 0 ? 0 : mantissaEnd - decimalIndex - 1;
            int trailingZeros = 0;
            int digitIndex = mantissaEnd - 1;
            while (digitIndex >= 0 && (token[digitIndex] == '0' || token[digitIndex] == '.'))
            {
                if (token[digitIndex] == '0')
                {
                    trailingZeros++;
                }
                digitIndex--;
            }
            if (digitIndex < 0 || token[digitIndex] == '-')
            {
                return true;
            }
            int exponent = 0;
            if (exponentIndex >= 0 && !int.TryParse(token.AsSpan(exponentIndex + 1),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            {
                return false;
            }
            // double への変換で微小な小数部分が消えても、整数として受理しない。
            return (long)exponent - fractionDigits + trailingZeros >= 0;
        }

        private static SfxParameters ApplyChanges(SfxParameters original, ChipKind chip, Dictionary<string, object> changes)
        {
            SfxParameters candidate = original;
            foreach (KeyValuePair<string, object> change in changes)
            {
                SfxParameterDescription description = SfxParameterCatalog.Get(change.Key, chip);
                candidate = description.Replace(candidate, change.Value);
            }
            return SfxParameterValidator.Normalize(candidate, chip);
        }
    }
}
