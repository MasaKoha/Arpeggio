using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli.Brief
{
    /// <summary>作曲指示書の解析・実行失敗を終了コードと単一 JSON に揃える。</summary>
    internal static class CliBriefExecution
    {
        internal static bool IsBriefCommand(string[] arguments)
        {
            return arguments.Length > 0 && arguments[0] == "brief";
        }

        internal static int ArgumentFailure(string[] arguments, ParseResult result)
        {
            // 値に飲み込まれた --json も、解析結果に依存せず検出する。
            bool json = arguments.Contains("--json");
            string operation = arguments.Length > 1 ? arguments[1] : "brief";
            string message = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
            return Failure(operation, json, CliExecution.OperationError, "InvalidParameter", message, "arguments");
        }

        internal static int Run(string operation, bool json, Func<string> action)
        {
            try
            {
                string output = action();
                Console.Write(output);
                if (!output.EndsWith("\n", StringComparison.Ordinal))
                {
                    Console.WriteLine();
                }
                return CliExecution.Success;
            }
            catch (CompositionBriefException exception)
            {
                int exitCode = exception.Code == "InvalidBrief" ? CliExecution.DocumentError : CliExecution.OperationError;
                return Failure(operation, json, exitCode, exception.Code, exception.Message, exception.ParameterPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure(operation, json, CliExecution.InputOutputError, "InputOutputError", exception.Message, "path");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                OverflowException or FormatException or JsonException)
            {
                return Failure(operation, json, CliExecution.OperationError, "InvalidParameter", exception.Message, "arguments");
            }
        }

        internal static string Saved(string operation, string path, CompositionBrief brief, bool json)
        {
            return json ? SessionOutput.Serialize(new { operation, path = Path.GetFullPath(path), brief }) : Path.GetFullPath(path);
        }

        private static int Failure(string operation, bool json, int exitCode, string code, string error, string parameterPath)
        {
            if (json)
            {
                Console.WriteLine(SessionOutput.Serialize(new { operation, error, exitCode, code, parameterPath }));
            }
            else
            {
                Console.Error.WriteLine($"{code}: {error} ({parameterPath})");
            }
            return exitCode;
        }
    }
}
