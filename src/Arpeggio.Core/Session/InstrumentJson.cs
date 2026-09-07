using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Session
{
    /// <summary>保存形式と同じ音色オブジェクトをツール入力として扱う。</summary>
    public static class InstrumentJson
    {
        /// <summary>kind の位置に依存せず音色を復元し、未知のパラメータを拒否する。</summary>
        public static Instrument Deserialize(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                string normalized = Normalize(document.RootElement);
                return JsonSerializer.Deserialize<Instrument>(normalized, CreateOptions())
                    ?? throw new ArgumentException("音色は null にできません。");
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                System.Collections.Generic.KeyNotFoundException or NotSupportedException or FormatException or OverflowException)
            {
                throw new ArgumentException("音色 JSON が不正です。kind とパラメータを確認してください。", exception);
            }
        }

        private static string Normalize(JsonElement root)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                JsonElement kind = root.GetProperty("kind");
                string kindName = kind.ValueKind == JsonValueKind.Number
                    ? ((InstrumentKind)kind.GetInt32()).ToString() : kind.GetString()!;
                writer.WriteString("kind", kindName);
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name != "kind")
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>音色を camelCase と文字列 enum の JSON にする。</summary>
        public static string Serialize(Instrument instrument)
        {
            return JsonSerializer.Serialize(instrument, CreateOptions());
        }

        internal static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
