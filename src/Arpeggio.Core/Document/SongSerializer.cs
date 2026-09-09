using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Document
{
    /// <summary>キー順とインデントを固定したソングの JSON 永続化。</summary>
    public static class SongSerializer
    {
        /// <summary>検証済みソングを差分比較用の正規 JSON にする。</summary>
        public static string Serialize(Song song)
        {
            SongValidator.Validate(song);
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(song, CreateOptions()));
            return RewriteInstruments(document.RootElement, false);
        }

        /// <summary>JSON の形式とソング制約を検証して復元する。</summary>
        public static Song Deserialize(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.GetProperty("version").GetInt32() != Song.CurrentVersion)
                {
                    throw new SongValidationException("version は 1 固定です。");
                }
                string normalized = RewriteInstruments(root, true);
                Song song = JsonSerializer.Deserialize<Song>(normalized, CreateOptions())
                    ?? throw new SongValidationException("ソングが null です。");
                SongValidator.Validate(song);
                return song;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException or NotSupportedException)
            {
                throw new SongValidationException("ソング JSON の形式が不正です。", exception);
            }
        }

        /// <summary>ファイルから検証済みソングを復元する。</summary>
        public static Song Load(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        /// <summary>同じディレクトリの一時ファイルを置換し、書き込み途中の観測を防ぐ。</summary>
        public static void Save(Song song, string path)
        {
            string json = Serialize(song);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
            return options;
        }

        private static string RewriteInstruments(JsonElement root, bool metadataFirst)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                bool hasSfx = false;
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name == "sfx")
                    {
                        SongValidator.Require(!hasSfx, "sfx は重複できません。");
                        hasSfx = true;
                    }
                    if (property.Name != "instruments" || property.Value.ValueKind != JsonValueKind.Array)
                    {
                        property.WriteTo(writer);
                        continue;
                    }
                    writer.WriteStartArray("instruments");
                    WriteInstruments(writer, property.Value, metadataFirst);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteInstruments(Utf8JsonWriter writer, JsonElement instruments, bool metadataFirst)
        {
            foreach (JsonElement instrument in instruments.EnumerateArray())
            {
                writer.WriteStartObject();
                if (metadataFirst)
                {
                    WriteKind(writer, instrument.GetProperty("kind"));
                }
                else
                {
                    writer.WritePropertyName("id");
                    instrument.GetProperty("id").WriteTo(writer);
                    writer.WritePropertyName("name");
                    instrument.GetProperty("name").WriteTo(writer);
                    WriteKind(writer, instrument.GetProperty("kind"));
                }
                WriteInstrumentProperties(writer, instrument, metadataFirst);
                writer.WriteEndObject();
            }
        }

        private static void WriteInstrumentProperties(Utf8JsonWriter writer, JsonElement instrument, bool metadataFirst)
        {
            foreach (JsonProperty property in instrument.EnumerateObject())
            {
                if (property.Name == "kind" || (!metadataFirst && (property.Name == "id" || property.Name == "name")))
                {
                    continue;
                }
                property.WriteTo(writer);
            }
        }

        private static void WriteKind(Utf8JsonWriter writer, JsonElement kind)
        {
            if (kind.ValueKind == JsonValueKind.Number)
            {
                writer.WriteString("kind", ((InstrumentKind)kind.GetInt32()).ToString());
                return;
            }
            writer.WritePropertyName("kind");
            kind.WriteTo(writer);
        }
    }
}
