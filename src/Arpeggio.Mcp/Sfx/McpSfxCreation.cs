using System;
using System.IO;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Session.Sfx;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Mcp.Sfx
{
    /// <summary>現在のセッションと履歴を変更せず、定義付き SFX を新規保存する。</summary>
    internal static class McpSfxCreation
    {
        internal static object Create(string path, string chip, string preset, string? title, bool dryRun)
        {
            RequireNewDestination(path);
            ChipKind selectedChip = ChipReference.ParseChip(chip);
            SfxParameterPresetDescription selectedPreset = SfxParameterPresetCatalog.Get(
                SfxParameterPresetCatalog.Parse(preset), selectedChip);
            SfxSongCompilationResult generation = SfxEditor.CreateCandidate(selectedPreset.Parameters,
                selectedChip, title ?? selectedPreset.Name, selectedPreset.Name);
            if (!dryRun)
            {
                SaveNew(path, generation.Song);
            }
            return SfxSessionOutput.Describe("create", generation.Song, generation, true, dryRun,
                dryRun ? null : SfxHash.ComputeRevision(generation.Song));
        }

        private static void RequireNewDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new SfxParameterException("InvalidParameter", "path", "保存先を指定してください。");
            }
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new SfxEditException("DestinationExists", "保存先が既に存在します。");
            }
        }

        private static void SaveNew(string path, Song candidate)
        {
            string content = SongSerializer.Serialize(candidate);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                // 存在確認後の競合でも、現在曲や第三者の保存結果を上書きしない。
                File.Move(temporaryPath, path, false);
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                throw new SfxEditException("DestinationExists", "保存先が既に存在します。");
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
