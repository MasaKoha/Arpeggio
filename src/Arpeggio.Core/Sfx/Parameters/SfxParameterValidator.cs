using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>生成を行わず、値・チップ適合性・包絡の条件を検証する。</summary>
    public static class SfxParameterValidator
    {
        /// <summary>秒数を制御フレームへ換算する周波数。</summary>
        public const int ControlFramesPerSecond = 60;

        /// <summary>正の repeat と decay が要求する最小秒数。</summary>
        public const double ControlFrameSeconds = 1.0 / ControlFramesPerSecond;

        /// <summary>各レイヤーの包絡フレーム数の上限。</summary>
        public const int MaximumEnvelopeFrames = 300;

        /// <summary>保存する実数の小数点以下桁数。</summary>
        public const int DecimalPlaces = 6;

        /// <summary>全値を検証し、拒否しない無音警告を返す。入力は変更しない。</summary>
        public static IReadOnlyList<SfxParameterWarning> Validate(SfxParameters parameters, ChipKind chip)
        {
            SfxParameters normalized = Normalize(parameters, chip);
            bool toneIsSilent = !normalized.Tone.Enabled || normalized.Tone.Envelope.Volume == 0;
            bool noiseIsSilent = !normalized.Noise.Enabled || normalized.Noise.Envelope.Volume == 0;
            if (!toneIsSilent || !noiseIsSilent)
            {
                return Array.Empty<SfxParameterWarning>();
            }
            return Array.AsReadOnly(new[]
            {
                new SfxParameterWarning
                {
                    Code = "SilentParameters",
                    Message = "全有効レイヤーのピーク音量が0です。"
                }
            });
        }

        /// <summary>範囲外を丸めで救済せず検証し、実数を6桁へ正規化した値を返す。</summary>
        public static SfxParameters Normalize(SfxParameters parameters, ChipKind chip)
        {
            ValidateStructure(parameters, chip);
            SfxParameters normalized = parameters;
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll(chip))
            {
                object original = description.Read(parameters);
                object value = NormalizeValue(description, original);
                if (!Equals(original, value) || IsNegativeZero(original))
                {
                    normalized = description.Replace(normalized, value);
                }
            }
            ValidateRelationships(normalized);
            return normalized;
        }

        internal static object NormalizeValue(SfxParameterDescription description, object value)
        {
            bool hasExpectedType = description.ValueKind switch
            {
                SfxParameterValueKind.Boolean => value is bool,
                SfxParameterValueKind.Integer => value is int,
                SfxParameterValueKind.Number => value is double,
                SfxParameterValueKind.Choice => value is string,
                _ => false
            };
            if (!hasExpectedType)
            {
                throw SfxParameterException.Invalid(description.Path, "パラメータの型が仕様と一致しません。");
            }
            ValidateChoices(description, value);
            if (value is not double && value is not int)
            {
                return value;
            }
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            bool isInRange = description.Minimum is double minimum && description.Maximum is double maximum
                && number >= minimum && number <= maximum;
            if (!double.IsFinite(number) || !(isInRange || (description.AllowsZero && number == 0)))
            {
                string range = FormattableString.Invariant($"{description.Minimum}〜{description.Maximum} {description.Unit}");
                string zero = description.AllowsZero ? "、または無効を表す0" : string.Empty;
                throw SfxParameterException.Invalid(description.Path, $"値は {range}{zero} の範囲で指定してください。");
            }
            if (value is int)
            {
                return value;
            }
            double rounded = Math.Round(number, DecimalPlaces, MidpointRounding.AwayFromZero);
            // 負のゼロを保存値の別表現にしないため、ゼロの符号も正規化する。
            return rounded == 0 ? 0.0 : rounded;
        }

        private static bool IsNegativeZero(object value)
        {
            return value is double number && number == 0 && BitConverter.DoubleToInt64Bits(number) < 0;
        }

        private static void ValidateChoices(SfxParameterDescription description, object value)
        {
            if (description.Choices.Count == 0)
            {
                return;
            }
            foreach (object choice in description.Choices)
            {
                if (Equals(choice, value))
                {
                    return;
                }
            }
            throw SfxParameterException.Invalid(description.Path, "仕様表の選択値を指定してください。");
        }

        private static void ValidateStructure(SfxParameters parameters, ChipKind chip)
        {
            SfxParameterCatalog.RequireChip(chip);
            if (parameters is null)
            {
                throw SfxParameterException.Invalid(string.Empty, "parameters は null にできません。");
            }
            if (parameters.Tone is null)
            {
                throw SfxParameterException.Invalid("tone", "tone は null にできません。");
            }
            if (parameters.Noise is null)
            {
                throw SfxParameterException.Invalid("noise", "noise は null にできません。");
            }
            RequireChipSettings("nes", chip == ChipKind.Nes, parameters.Nes);
            RequireChipSettings("gameBoy", chip == ChipKind.GameBoy, parameters.GameBoy);
            RequireChipSettings("snes", chip == ChipKind.Snes, parameters.Snes);
            RequireEnvelope(parameters.Tone.Envelope, "tone.envelope");
            RequireEnvelope(parameters.Noise.Envelope, "noise.envelope");
        }

        private static void RequireChipSettings(string path, bool isCurrentChip, object? settings)
        {
            if (isCurrentChip && settings is null)
            {
                throw SfxParameterException.Invalid(path, "現在チップの固有設定が必要です。");
            }
            if (!isCurrentChip && settings != null)
            {
                throw SfxParameterException.Unsupported(path);
            }
        }

        private static void RequireEnvelope(SfxEnvelopeParameters envelope, string path)
        {
            if (envelope is null)
            {
                throw SfxParameterException.Invalid(path, "包絡は null にできません。");
            }
        }

        private static void ValidateRelationships(SfxParameters parameters)
        {
            if (!parameters.Tone.Enabled && !parameters.Noise.Enabled)
            {
                throw SfxParameterException.Invalid("tone.enabled", "最低一つのレイヤーを有効にしてください。");
            }
            ValidateEnvelope(parameters.Tone.Envelope, "tone.envelope");
            ValidateEnvelope(parameters.Noise.Envelope, "noise.envelope");
        }

        private static void ValidateEnvelope(SfxEnvelopeParameters envelope, string path)
        {
            int attackFrames = ToFrames(envelope.AttackSeconds);
            int sustainFrames = ToFrames(envelope.SustainSeconds);
            int decayFrames = ToFrames(envelope.DecaySeconds);
            if (attackFrames + sustainFrames + decayFrames > MaximumEnvelopeFrames)
            {
                throw SfxParameterException.Invalid(path, "包絡は合計300制御フレーム以下にしてください。");
            }
            if (envelope.Punch > 0 && sustainFrames == 0)
            {
                throw SfxParameterException.Invalid(path + ".punch", "punch には量子化後1フレーム以上の sustain が必要です。");
            }
        }

        private static int ToFrames(double seconds)
        {
            return (int)Math.Round(seconds * ControlFramesPerSecond, MidpointRounding.AwayFromZero);
        }
    }
}
