using System;
using System.IO;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>定義付きの生成ソングと保存先をテスト単位で隔離する。</summary>
    internal sealed class SfxEditFixture : IDisposable
    {
        internal SfxEditFixture(ChipKind chip = ChipKind.Nes, Song? initial = null)
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "arpeggio-sfx-edit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(DirectoryPath, "song.arpeggio.json");
            try
            {
                SongSerializer.Save(initial ?? CreateSong(chip), Path);
                Session.Open(Path);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal string DirectoryPath { get; }
        internal string Path { get; }
        internal EditSession Session { get; } = new EditSession();
        internal Song Song => Session.Song!;

        internal static Song CreateSong(ChipKind chip = ChipKind.Nes)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(chip);
            return SfxEditor.CreateCandidate(parameters, chip, "効果音の正本", "jump", new SfxRandomization
            {
                Operation = SfxRandomizationOperation.Mutate,
                Seed = uint.MaxValue,
                Strength = 0.1,
                Locks = new[] { "tone.baseFrequencyHz" },
                BaseParametersHash = SfxHash.ComputeParametersHash(parameters, chip)
            }).Song;
        }

        internal static Song CreateUnsupportedSong(string versionProperty)
        {
            JsonObject document = SfxDocumentTestData.ReadDocument(CreateSong());
            JsonObject definition = SfxDocumentTestData.Definition(document);
            const int FutureVersion = 99;
            if (versionProperty == "algorithmVersion")
            {
                definition["lastRandomization"]![versionProperty] = FutureVersion;
            }
            else
            {
                definition[versionProperty] = FutureVersion;
            }
            definition["future"] = JsonNode.Parse("{\"values\":[3,1,2],\"label\":\"未解釈\"}");
            return SongSerializer.Deserialize(document.ToJsonString());
        }

        /// <summary>保存失敗テストで削除済みの場合も、残った一時ファイルを後始末する。</summary>
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
