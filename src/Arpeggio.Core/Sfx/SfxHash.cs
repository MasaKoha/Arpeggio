using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>環境の改行に依存しない正規 JSON の SHA-256 指紋。</summary>
    public static class SfxHash
    {
        private const int CanonicalIndentSize = 2;

        /// <summary>版と全パラメータを規定順に正規化する。出自は含めない。</summary>
        public static string ComputeParametersHash(SfxParameters parameters, ChipKind chip,
            int schemaVersion = SfxDefinitionData.CurrentSchemaVersion,
            int generatorVersion = SfxDefinitionData.CurrentGeneratorVersion)
        {
            SongValidator.Require(schemaVersion > 0 && generatorVersion > 0, "sfx の版は正の整数です。");
            SfxParameters normalized = SfxParameterValidator.Normalize(parameters, chip);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, CreateWriterOptions()))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", schemaVersion);
                writer.WriteNumber("generatorVersion", generatorVersion);
                writer.WritePropertyName("parameters");
                SfxParameterJson.Write(writer, normalized);
                writer.WriteEndObject();
            }
            return ComputeHash(stream.ToArray());
        }

        /// <summary>title と sfx だけを除き、音色名・空トラック・定位・エコーも含める。</summary>
        public static string ComputeGeneratedHash(Song song)
        {
            using JsonDocument document = JsonDocument.Parse(SongSerializer.Serialize(song));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, CreateWriterOptions()))
            {
                writer.WriteStartObject();
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (property.Name != "title" && property.Name != "sfx")
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return ComputeHash(stream.ToArray());
        }

        /// <summary>sfx と title を含むソング全体の指紋。編集競合の照合に使う。</summary>
        public static string ComputeRevision(Song song)
        {
            using JsonDocument document = JsonDocument.Parse(SongSerializer.Serialize(song));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, CreateWriterOptions()))
            {
                document.RootElement.WriteTo(writer);
            }
            return ComputeHash(stream.ToArray());
        }

        private static JsonWriterOptions CreateWriterOptions()
        {
            return new JsonWriterOptions { Indented = true, IndentSize = CanonicalIndentSize, NewLine = "\n" };
        }

        private static string ComputeHash(byte[] bytes)
        {
            return System.Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
    }
}
