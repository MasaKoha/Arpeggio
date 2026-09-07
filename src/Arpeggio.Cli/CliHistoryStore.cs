using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>CLI 呼び出し間の履歴を colors と同じ側車ファイルへ保存する。</summary>
    internal static class CliHistoryStore
    {
        internal static void Load(EditSession session)
        {
            string path = GetPath(session);
            if (!File.Exists(path))
            {
                return;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement;
                // MCP や外部エディターが更新した曲には古い履歴を適用しない。
                if (root.GetProperty("current").GetString() != SongSerializer.Serialize(session.Song!))
                {
                    return;
                }
                Song[] undo = root.GetProperty("undo").EnumerateArray()
                    .Select(element => SongSerializer.Deserialize(element.GetString()!)).ToArray();
                Song[] redo = root.GetProperty("redo").EnumerateArray()
                    .Select(element => SongSerializer.Deserialize(element.GetString()!)).ToArray();
                session.History.Restore(undo, redo);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                KeyNotFoundException or ArgumentException or SongValidationException)
            {
                throw new SongValidationException("履歴ファイルが不正です。<path>.history を退避してから再実行してください。", exception);
            }
        }

        internal static void Save(EditSession session)
        {
            string path = GetPath(session);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = SessionOutput.Serialize(new
            {
                current = SongSerializer.Serialize(session.Song!),
                undo = session.History.GetUndoSnapshots().Select(SongSerializer.Serialize).ToArray(),
                redo = session.History.GetRedoSnapshots().Select(SongSerializer.Serialize).ToArray()
            });
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
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

        private static string GetPath(EditSession session)
        {
            return session.Path + ".history/state.json";
        }
    }
}
