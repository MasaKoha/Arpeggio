using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Mcp.Sfx
{
    /// <summary>新 SFX ツールの失敗だけを、CLI と同じコード・位置を持つ JSON 文字列へ変換する。</summary>
    internal static class McpSfxExecution
    {
        private const int OperationError = 1;
        private const int DocumentError = 2;
        private const int InputOutputError = 3;

        internal static string Run(string operation, Func<object> action)
        {
            try
            {
                return SessionOutput.Serialize(action());
            }
            catch (SongValidationException exception)
            {
                return Failure(operation, DocumentError, "InvalidSong", exception.Message, "document");
            }
            catch (SfxParameterException exception)
            {
                return Failure(operation, OperationError, exception.Code, exception.Message, exception.ParameterPath);
            }
            catch (SfxEditException exception)
            {
                return Failure(operation, OperationError, exception.Code, exception.Message,
                    exception.Code == "RevisionConflict" ? "expectedRevision" : "sfx");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure(operation, InputOutputError, "InputOutputError", exception.Message, "path");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                OverflowException or FormatException or JsonException)
            {
                return Failure(operation, OperationError, "InvalidParameter", exception.Message, "arguments");
            }
        }

        private static string Failure(string operation, int exitCode, string code, string error, string parameterPath)
        {
            return SessionOutput.Serialize(new { operation, error, exitCode, code, parameterPath });
        }
    }
}
