using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Session;

namespace Arpeggio.Mcp.Brief
{
    /// <summary>既存 MCP と共有するロックの内側で、作曲指示書の失敗を JSON に揃える。</summary>
    internal static class McpBriefExecution
    {
        private const int OperationError = 1;
        private const int DocumentError = 2;
        private const int InputOutputError = 3;

        internal static string Run(EditSession session, string operation, Func<string> action)
        {
            // ツールインスタンスが異なっても、既存 Song 操作と同じ直列実行の境界を使う。
            lock (session)
            {
                return RunLocked(operation, action);
            }
        }

        private static string RunLocked(string operation, Func<string> action)
        {
            try
            {
                return action();
            }
            catch (CompositionBriefException exception)
            {
                int exitCode = exception.Code == "InvalidBrief" ? DocumentError : OperationError;
                return Failure(operation, exitCode, exception.Code, exception.Message, exception.ParameterPath);
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
