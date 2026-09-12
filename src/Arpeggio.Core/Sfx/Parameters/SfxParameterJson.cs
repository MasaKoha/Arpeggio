using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>完全な保存パラメータの読み取りと、型の固定順による正規出力。</summary>
    internal static class SfxParameterJson
    {
        internal static SfxParameters Read(JsonElement element)
        {
            ChipKind chip = ReadChip(element);
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll(chip))
            {
                RequirePath(element, description.Path);
            }
            // 全キーの存在確認を先に行い、patch の既定値で欠落を補完しない。
            return SfxParameterPatch.Apply(SfxParameterCatalog.CreateDefaults(chip), chip, element.GetRawText()).Parameters;
        }

        internal static void Write(Utf8JsonWriter writer, SfxParameters parameters)
        {
            JsonSerializerOptions options = CreateOptions();
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            JsonSerializer.Serialize(writer, parameters, options);
        }

        internal static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        internal static ChipKind GetChip(SfxParameters parameters)
        {
            if (parameters.Nes != null)
            {
                return ChipKind.Nes;
            }
            return parameters.GameBoy != null ? ChipKind.GameBoy : ChipKind.Snes;
        }

        private static ChipKind ReadChip(JsonElement element)
        {
            SongValidator.Require(element.ValueKind == JsonValueKind.Object, "sfx.parameters はオブジェクトです。");
            bool hasNes = element.TryGetProperty("nes", out _);
            bool hasGameBoy = element.TryGetProperty("gameBoy", out _);
            bool hasSnes = element.TryGetProperty("snes", out _);
            int chipCount = (hasNes ? 1 : 0) + (hasGameBoy ? 1 : 0) + (hasSnes ? 1 : 0);
            SongValidator.Require(chipCount == 1, "sfx.parameters には現在チップ一つの固有設定が必要です。");
            if (hasNes)
            {
                return ChipKind.Nes;
            }
            return hasGameBoy ? ChipKind.GameBoy : ChipKind.Snes;
        }

        private static void RequirePath(JsonElement element, string path)
        {
            foreach (string segment in path.Split('.'))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out JsonElement child))
                {
                    throw new SongValidationException($"sfx.parameters.{path} が欠落しています。");
                }
                element = child;
            }
        }
    }
}
