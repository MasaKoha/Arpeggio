using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>SFX の引数解析と実行失敗を単一の JSON 応答へ揃える。</summary>
    internal static class CliSfxExecution
    {
        internal static bool IsSfxCommand(string[] arguments)
        {
            return arguments.Length >= 2 && arguments[0] == "sfx" && arguments[1] != "new";
        }

        internal static int ArgumentFailure(string[] arguments, ParseResult result)
        {
            // 解析エラーの原因トークンが直前オプションの値として飲み込まれ、
            // --json 自身の OptionResult が生成されないことがあるため、
            // 解析結果ではなく生の引数配列で --json の有無を判定する。
            bool json = arguments.Contains("--json");
            string message = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
            return Failure(arguments[1], json, CliExecution.OperationError, "InvalidParameter", message, "arguments");
        }

        internal static int Run(string operation, bool json, Func<object> action)
        {
            try
            {
                object output = action();
                if (json)
                {
                    Console.WriteLine(SessionOutput.Serialize(output));
                }
                else
                {
                    SfxOutput.WriteText(output);
                }
                return CliExecution.Success;
            }
            catch (SongValidationException exception)
            {
                return Failure(operation, json, CliExecution.DocumentError, "InvalidSong", exception.Message, "document");
            }
            catch (SfxParameterException exception)
            {
                return Failure(operation, json, CliExecution.OperationError, exception.Code, exception.Message, exception.ParameterPath);
            }
            catch (SfxEditException exception)
            {
                return Failure(operation, json, CliExecution.OperationError, exception.Code, exception.Message,
                    exception.Code == "RevisionConflict" ? "expectedRevision" : "sfx");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure(operation, json, CliExecution.InputOutputError, "InputOutputError", exception.Message, "path");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                OverflowException or FormatException or System.Text.Json.JsonException)
            {
                return Failure(operation, json, CliExecution.OperationError, "InvalidParameter", exception.Message, "arguments");
            }
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
