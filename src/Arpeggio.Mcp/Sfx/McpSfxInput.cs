using System.Collections.Generic;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Session.Sfx;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Mcp.Sfx
{
    /// <summary>MCP 固有の未オープン読み取りと文字列 JSON 入力を Core の契約へ接続する。</summary>
    internal static class McpSfxInput
    {
        internal static object Parameters(Song? song, bool includeSchema, string? chip)
        {
            if (song is null)
            {
                if (chip is null)
                {
                    throw new SfxParameterException("InvalidParameter", "chip", "未オープンで初期値と schema を取得するにはチップを指定してください。");
                }
                ChipKind selectedChip = ChipReference.ParseChip(chip);
                var defaults = SfxEditor.CreateCandidate(
                    SfxParameterCatalog.CreateDefaults(selectedChip), selectedChip);
                var initial = SfxSessionOutput.Describe("params", defaults.Song, defaults);
                initial["candidateRevision"] = null;
                initial["schema"] = SfxSessionOutput.Schema(selectedChip);
                return initial;
            }
            if (chip is not null && ChipReference.ParseChip(chip) != song.Chip)
            {
                throw new SfxParameterException("UnsupportedParameter", "chip", "保存済み Song と異なるチップは指定できません。");
            }
            SfxSynchronizationState synchronization = SfxSynchronization.Inspect(song);
            SfxSongCompilationResult? generation = synchronization.Editable
                ? SfxSongCompiler.Compile(synchronization.Parameters!, song.Chip, song.Title) : null;
            var output = SfxSessionOutput.Describe("params", song, generation, revision: SfxHash.ComputeRevision(song));
            if (includeSchema)
            {
                output["schema"] = SfxSessionOutput.Schema(song.Chip);
            }
            return output;
        }

        internal static IReadOnlyList<string>? ParseLocks(string? locks)
        {
            if (locks is null)
            {
                return null;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(locks);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    throw InvalidLocks();
                }
                var paths = new List<string>();
                foreach (JsonElement item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        throw InvalidLocks();
                    }
                    paths.Add(item.GetString()!);
                }
                return paths;
            }
            catch (JsonException exception)
            {
                throw new SfxParameterException("InvalidParameter", "locks", "locks は正規パスの JSON 文字列配列で指定してください。", exception);
            }
        }

        private static SfxParameterException InvalidLocks()
        {
            return new SfxParameterException("InvalidParameter", "locks", "locks は正規パスの JSON 文字列配列で指定してください。");
        }
    }
}
