using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Daw.Presenters.Sfx
{
    /// <summary>表示用の数値を共通仕様のパス・対数軸・キー刻みへ変換する。</summary>
    public static class SfxParameterInput
    {
        private const double SemitonesPerOctave = 12;
        private const int DecimalPlaces = 6;

        /// <summary>保存形式のパラメータから表示値を読む。</summary>
        public static object Read(JsonElement parameters, SfxParameterDescription description)
        {
            foreach (string segment in description.Path.Split('.'))
            {
                parameters = parameters.GetProperty(segment);
            }
            return parameters.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => parameters.GetString()!,
                _ => parameters.GetDouble()
            };
        }

        /// <summary>小数6桁を省略せず必要な桁だけ表示する。</summary>
        public static string Format(object value) => value is double number
            ? number.ToString("0.######", CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        /// <summary>不正な文字も保持し、Core が該当パスを診断できる JSON 値へ変換する。</summary>
        public static object ParseText(string text)
        {
            // 整数の端数や極小指数を double で失ってから検証しない。
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Number) { return document.RootElement.Clone(); }
            }
            catch (JsonException) { }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
            {
                return number;
            }
            return text;
        }

        /// <summary>複数欄の未確定値を一つの部分 patch にまとめる。</summary>
        public static string CreatePatch(IReadOnlyDictionary<string, object> values)
        {
            var root = new JsonObject();
            foreach (var entry in values)
            {
                string[] segments = entry.Key.Split('.');
                JsonObject parent = root;
                foreach (string segment in segments.Take(segments.Length - 1))
                {
                    if (parent[segment] is not JsonObject child)
                    {
                        child = new JsonObject();
                        parent[segment] = child;
                    }
                    parent = child;
                }
                parent[segments[^1]] = JsonSerializer.SerializeToNode(entry.Value);
            }
            return root.ToJsonString();
        }

        /// <summary>周波数だけ対数座標へ変換する。</summary>
        public static double ToSlider(SfxParameterDescription description, double value) =>
            description.IsLogarithmic ? Math.Log2(value) : value;

        /// <summary>スライダー座標を入力値へ戻し、整数と離散したゼロ境界を扱う。</summary>
        public static double FromSlider(SfxParameterDescription description, double position)
        {
            double value = description.IsLogarithmic ? Math.Pow(2, position) : position;
            if (description.AllowsZero && value < description.Minimum!.Value)
            {
                value = value < description.Minimum.Value / 2 ? 0 : description.Minimum.Value;
            }
            if (description.ValueKind == SfxParameterValueKind.Integer)
            {
                value = Math.Round(value, MidpointRounding.AwayFromZero);
            }
            return Math.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
        }

        /// <summary>通常／Shift のキー刻みを適用する。周波数は半音、選択項目は一段で進む。</summary>
        public static object Step(SfxParameterDescription description, object current, int direction, bool fine)
        {
            if (description.Choices.Count > 0)
            {
                int index = description.Choices.ToList().FindIndex(choice => Format(choice) == Format(current));
                return description.Choices[Math.Clamp(index + direction, 0, description.Choices.Count - 1)];
            }
            double value = Convert.ToDouble(current, CultureInfo.InvariantCulture);
            double step = fine ? description.FineStep : description.Step;
            double next = description.IsLogarithmic
                ? value * Math.Pow(2, direction * step / SemitonesPerOctave)
                : value + direction * step;
            if (description.AllowsZero && next < description.Minimum!.Value)
            {
                next = direction > 0 ? description.Minimum.Value : 0;
            }
            next = Math.Clamp(next, description.AllowsZero ? 0 : description.Minimum!.Value, description.Maximum!.Value);
            return Math.Round(next, DecimalPlaces, MidpointRounding.AwayFromZero);
        }
    }
}
