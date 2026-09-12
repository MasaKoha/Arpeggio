using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;

namespace Arpeggio.Core.Sfx.Storage
{
    /// <summary>版を先に判定し、既知 DTO と未知 JSON の保存経路を分ける。</summary>
    internal sealed class SfxDefinitionJsonConverter : JsonConverter<SfxDefinition>
    {
        /// <summary>属性から生成される保存用コンバーター。</summary>
        public SfxDefinitionJsonConverter() { }

        /// <summary>未知版には現在版の構造検証を適用せず、全体を保持する。</summary>
        public override SfxDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (ReadVersion(root, "schemaVersion") != SfxDefinitionData.CurrentSchemaVersion
                || ReadVersion(root, "generatorVersion") != SfxDefinitionData.CurrentGeneratorVersion
                || HasUnsupportedRandomization(root))
            {
                return new SfxDefinition(root);
            }
            RequireProperties(root, "schemaVersion", "generatorVersion", "parameters", "parametersHash",
                "sourcePreset", "lastRandomization", "generatedHash");
            SfxParameters parameters = SfxParameterJson.Read(root.GetProperty("parameters"));
            var definition = new SfxDefinition(new SfxDefinitionData
            {
                Parameters = parameters,
                ParametersHash = root.GetProperty("parametersHash").GetString()!,
                SourcePreset = root.GetProperty("sourcePreset").GetString(),
                LastRandomization = ReadRandomization(root.GetProperty("lastRandomization")),
                GeneratedHash = root.GetProperty("generatedHash").GetString()!
            });
            SfxDefinitionValidator.Validate(definition, SfxParameterJson.GetChip(parameters));
            return definition;
        }

        /// <summary>既知版は規定順、未知版は未解釈のキーと値を保存する。</summary>
        public override void Write(Utf8JsonWriter writer, SfxDefinition value, JsonSerializerOptions options)
        {
            if (value.Known is not SfxDefinitionData data)
            {
                value.UnsupportedJson!.Value.WriteTo(writer);
                return;
            }
            SfxParameters normalized = SfxParameterValidator.Normalize(data.Parameters, SfxParameterJson.GetChip(data.Parameters));
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", data.SchemaVersion);
            writer.WriteNumber("generatorVersion", data.GeneratorVersion);
            writer.WritePropertyName("parameters");
            SfxParameterJson.Write(writer, normalized);
            writer.WriteString("parametersHash", data.ParametersHash);
            writer.WriteString("sourcePreset", data.SourcePreset);
            writer.WritePropertyName("lastRandomization");
            JsonSerializer.Serialize(writer, data.LastRandomization, SfxParameterJson.CreateOptions());
            writer.WriteString("generatedHash", data.GeneratedHash);
            writer.WriteEndObject();
        }

        private static bool HasUnsupportedRandomization(JsonElement root)
        {
            JsonElement randomization = root.GetProperty("lastRandomization");
            return randomization.ValueKind != JsonValueKind.Null
                && ReadVersion(randomization, "algorithmVersion") != SfxRandomization.CurrentAlgorithmVersion;
        }

        private static SfxRandomization? ReadRandomization(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            RequireProperties(element, "operation", "algorithmVersion", "seed", "category", "strength", "locks", "baseParametersHash");
            string? operation = element.GetProperty("operation").GetString();
            SongValidator.Require(operation == "randomize" || operation == "mutate",
                "sfx.lastRandomization.operation は正式名で指定してください。");
            SfxRandomization randomization = JsonSerializer.Deserialize<SfxRandomization>(element.GetRawText(), SfxParameterJson.CreateOptions())!;
            // 読み取った配列を IReadOnlyList の公開口から書き換えられないようにする。
            return randomization.Locks is null ? randomization
                : randomization with { Locks = new List<string>(randomization.Locks).AsReadOnly() };
        }

        private static int ReadVersion(JsonElement element, string name)
        {
            SongValidator.Require(element.ValueKind == JsonValueKind.Object, "sfx の版を持つ値はオブジェクトです。");
            int count = 0;
            int version = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name != name)
                {
                    continue;
                }
                count++;
                SongValidator.Require(property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out version) && version > 0, $"sfx.{name} は正の整数です。");
            }
            SongValidator.Require(count == 1, $"sfx.{name} は省略・重複できません。");
            return version;
        }

        private static void RequireProperties(JsonElement element, params string[] expected)
        {
            SongValidator.Require(element.ValueKind == JsonValueKind.Object, "sfx はオブジェクトです。");
            var remaining = new HashSet<string>(expected, StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                SongValidator.Require(remaining.Remove(property.Name), $"sfx の未知または重複キー '{property.Name}' は不正です。");
            }
            SongValidator.Require(remaining.Count == 0, "sfx の必須キーが欠落しています。");
        }
    }
}
