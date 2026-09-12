using System;
using System.IO;
using System.Linq;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Session.Sfx;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>新規作成と側車履歴初期化をまとめ、失敗時に開始前のファイル状態へ戻す。</summary>
    internal static class SfxFileTransaction
    {
        internal static void RequireNewDestination(string path)
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

        internal static void Create(string path, Song candidate)
        {
            RequireNewDestination(path);
            byte[] content = new UTF8Encoding(false).GetBytes(SongSerializer.Serialize(candidate));
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string historyDirectory = path + ".history";
            bool historyDirectoryExisted = Directory.Exists(historyDirectory);
            bool created = false;
            try
            {
                File.WriteAllBytes(temporaryPath, content);
                try
                {
                    File.Move(temporaryPath, path, false);
                }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path))
                {
                    throw new SfxEditException("DestinationExists", "保存先が既に存在します。");
                }
                created = true;
                EditSession session = CliExecution.Open(path);
                CliHistoryStore.Save(session);
            }
            catch
            {
                // 作成後に第三者が編集したファイルは、失敗の後始末で削除しない。
                if (created && File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(content))
                {
                    File.Delete(path);
                }
                RemoveEmptyHistoryDirectory(historyDirectory, historyDirectoryExisted);
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static object Edit(string path, Func<EditSession, SfxEditResult> edit)
        {
            string historyDirectory = path + ".history";
            bool historyDirectoryExisted = Directory.Exists(historyDirectory);
            SfxEditResult? result = null;
            try
            {
                CliExecution.Edit(path, session => { result = edit(session); },
                    () => result!.Changed && !result.DryRun);
                return SfxSessionOutput.Edit(result!);
            }
            finally
            {
                RemoveEmptyHistoryDirectory(historyDirectory, historyDirectoryExisted);
            }
        }

        private static void RemoveEmptyHistoryDirectory(string path, bool existed)
        {
            if (!existed && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
    }
}
