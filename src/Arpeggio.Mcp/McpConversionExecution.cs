using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Formats;

namespace Arpeggio.Mcp
{
    /// <summary>新しい変換ツールの診断と失敗分類を JSON 文字列へまとめる。</summary>
    internal sealed class McpConversionExecution
    {
        private const int Success = 0;
        private const int OperationError = 1;
        private const int DocumentError = 2;
        private const int InputOutputError = 3;
        private readonly string path;
        private readonly bool dryRun;
        private bool destinationExists;

        internal McpConversionExecution(ConversionFormat format, string path, bool strict, bool dryRun)
        {
            Report = new ConversionReport(format, ChipKind.None, strict);
            this.path = path;
            this.dryRun = dryRun;
        }

        internal ConversionReport Report { get; set; }
        internal bool Written { get; set; }

        internal string Run(Action action)
        {
            try
            {
                destinationExists = File.Exists(path) || Directory.Exists(path);
                action();
                if (!Report.CanWrite)
                {
                    return Failure(OperationError, "ConversionRejected", "変換エラーまたは strict 警告により保存できません。");
                }
                return SessionOutput.Serialize(new
                {
                    path, written = Written, dryRun, destinationExists, exitCode = Success, report = Report
                });
            }
            catch (SongValidationException exception)
            {
                return Failure(DocumentError, "InvalidSong", exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                destinationExists = File.Exists(path) || Directory.Exists(path);
                return Failure(InputOutputError, "InputOutputError", exception.Message);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                FormatException or OverflowException or JsonException)
            {
                return Failure(OperationError, "InvalidOptions", exception.Message);
            }
        }

        internal void ValidateDestination(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("保存先を指定してください。");
            }
            string destination = Path.GetFullPath(path);
            if (sourcePath != null && string.Equals(destination, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("入力と同じパスには保存できません。");
            }
        }

        private string Failure(int exitCode, string code, string error)
        {
            return SessionOutput.Serialize(new
            {
                path, written = Written, dryRun, destinationExists, code, error, exitCode, report = Report
            });
        }
    }
}
