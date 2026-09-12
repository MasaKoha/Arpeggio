using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>生成器に依存せず保存契約を検証するためのソングと定義。</summary>
    internal static class SfxDocumentTestData
    {
        internal static Song CreateSong(ChipKind chip = ChipKind.Nes)
        {
            Song song = SfxPresetFactory.Create(chip, SfxPresetKind.Jump);
            song.Sfx = new SfxDefinition(CreateDefinition(song));
            return song;
        }

        internal static SfxDefinitionData CreateDefinition(Song song)
        {
            // C1 は生成列とパラメータの同値性を証明せず、保存した指紋だけを照合する。
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(song.Chip);
            return new SfxDefinitionData
            {
                Parameters = parameters,
                ParametersHash = SfxHash.ComputeParametersHash(parameters, song.Chip),
                SourcePreset = "jump",
                GeneratedHash = SfxHash.ComputeGeneratedHash(song)
            };
        }

        internal static JsonObject ReadDocument(Song song)
        {
            return JsonNode.Parse(SongSerializer.Serialize(song))!.AsObject();
        }

        internal static JsonObject Definition(JsonObject document)
        {
            return document["sfx"]!.AsObject();
        }

        internal static JsonObject Parent(JsonObject root, string path, out string propertyName)
        {
            string[] segments = path.Split('.');
            JsonObject parent = root;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                parent = parent[segments[index]]!.AsObject();
            }
            propertyName = segments[segments.Length - 1];
            return parent;
        }
    }
}
