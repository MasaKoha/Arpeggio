using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>引数の実行・例外分類・永続履歴の境界。</summary>
    public static class CliExecution
    {
        internal const int Success = 0;
        private const int OperationError = 1;
        private const int DocumentError = 2;
        private const int InputOutputError = 3;

        /// <summary>プロセスを起動せずコマンドを実行し、0/1/2/3 の終了コードを返す。</summary>
        public static int Run(string[] arguments)
        {
            return Run(() => CommandFactory.Create().Parse(arguments).Invoke());
        }

        internal static int Run(Func<int> action)
        {
            try
            {
                return action();
            }
            catch (SongValidationException exception)
            {
                Console.Error.WriteLine($"{exception.Message} ドキュメントの形式と制約を確認してください。");
                return DocumentError;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"入出力に失敗しました: {exception.Message} パス・権限・空き容量を確認してください。");
                return InputOutputError;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                OverflowException or FormatException or JsonException)
            {
                Console.Error.WriteLine(exception.Message);
                return OperationError;
            }
        }

        internal static EditSession Open(string path)
        {
            EditSession session = new EditSession();
            session.Open(path);
            return session;
        }

        private static void RestoreFile(string path, byte[] content)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, content);
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

        internal static int Edit(string path, Action<EditSession> action)
        {
            EditSession session = Open(path);
            CliHistoryStore.Load(session);
            byte[] before = File.ReadAllBytes(path);
            action(session);
            try
            {
                CliHistoryStore.Save(session);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 履歴を書けなかった編集を成功状態の曲だけとして残さない。
                RestoreFile(path, before);
                throw;
            }
            return Success;
        }
    }
}
