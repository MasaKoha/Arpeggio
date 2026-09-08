using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Formats;

namespace Arpeggio.Cli
{
    /// <summary>変換コマンドの report・出力先状態・例外分類を一つの応答へまとめる。</summary>
    internal sealed class CliConversionExecution
    {
        internal CliConversionExecution(ConversionReport report, string path, bool json, bool dryRun)
        {
            Report = report;
            Path = path;
            Json = json;
            DryRun = dryRun;
        }

        internal ConversionReport Report { get; set; }
        internal string Path { get; }
        internal bool Json { get; }
        internal bool DryRun { get; }
        internal bool DestinationExists { get; private set; }
        internal bool Written { get; set; }

        internal int Run(Action action)
        {
            try
            {
                DestinationExists = File.Exists(Path) || Directory.Exists(Path);
                action();
                if (!Report.CanWrite)
                {
                    return Complete(CliExecution.OperationError, "ConversionRejected", "変換エラーまたは strict 警告により保存できません。");
                }
                return Complete(CliExecution.Success);
            }
            catch (SongValidationException exception)
            {
                return Complete(CliExecution.DocumentError, "InvalidSong", exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DestinationExists = File.Exists(Path) || Directory.Exists(Path);
                return Complete(CliExecution.InputOutputError, "InputOutputError", exception.Message);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                OverflowException or FormatException or JsonException)
            {
                return Complete(CliExecution.OperationError, "InvalidOptions", exception.Message);
            }
        }

        internal void ValidateDestination(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                throw new ArgumentException("保存先を指定してください。");
            }
            if (string.Equals(System.IO.Path.GetFullPath(Path), System.IO.Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("入力と同じパスには保存できません。");
            }
        }

        internal static bool IsConversionCommand(string[] arguments)
        {
            return arguments.Length >= 2 &&
                ((arguments[0] == "export" && arguments[1] is "nsf" or "vgm") ||
                 (arguments[0] == "import" && arguments[1] == "midi"));
        }

        internal static int ArgumentFailure(string[] arguments, ParseResult result)
        {
            string message = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
            ConversionFormat format = arguments[1] switch
            {
                "nsf" => ConversionFormat.Nsf,
                "vgm" => ConversionFormat.Vgm,
                _ => ConversionFormat.Midi
            };
            var report = new ConversionReport(format, ChipKind.None, IsFlagEnabled(result, "--strict"));
            report.AddError(new ConversionDiagnostic("InvalidArguments", message));
            var execution = new CliConversionExecution(report, string.Empty,
                IsFlagEnabled(result, "--json"), IsFlagEnabled(result, "--dry-run"));
            return execution.Complete(CliExecution.OperationError, "InvalidArguments", message);
        }

        private static bool IsFlagEnabled(ParseResult result, string name)
        {
            if (result.GetResult(name) is not OptionResult option)
            {
                return false;
            }
            return option.Errors.Count() > 0 || option.GetValueOrDefault<bool>();
        }

        private int Complete(int exitCode, string? code = null, string? error = null)
        {
            if (Json)
            {
                Console.WriteLine(SessionOutput.Serialize(new
                {
                    path = Path, written = Written, dryRun = DryRun, destinationExists = DestinationExists,
                    exitCode, code, error, report = Report
                }));
                return exitCode;
            }
            string status = DryRun ? "変換確認" : "保存完了";
            if (exitCode != CliExecution.Success)
            {
                status = "変換失敗";
            }
            Console.WriteLine($"{status}: {Path} | {Report.Format} / {Report.Chip} | {Report.DurationSeconds:F6} 秒 | {Report.OutputBytes} byte | 保存先あり={DestinationExists}");
            foreach (string limitation in Report.Limitations)
            {
                Console.WriteLine($"制限: {limitation}");
            }
            foreach (ConversionDiagnostic diagnostic in Report.Warnings)
            {
                WriteDiagnostic(diagnostic);
            }
            foreach (ConversionDiagnostic diagnostic in Report.Errors)
            {
                WriteDiagnostic(diagnostic);
            }
            if (Report.DroppedWarningCount != 0 || Report.DroppedErrorCount != 0)
            {
                Console.Error.WriteLine($"明細保持上限超過: 警告 {Report.DroppedWarningCount} / エラー {Report.DroppedErrorCount}");
            }
            if (error != null)
            {
                Console.Error.WriteLine($"{code}: {error}");
            }
            return exitCode;
        }

        private static void WriteDiagnostic(ConversionDiagnostic diagnostic)
        {
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message} " +
                $"(track={diagnostic.SourceTrack}, channel={diagnostic.SourceChannel}, event={diagnostic.SourceEvent}, tick={diagnostic.SourceTick}, " +
                $"outputTrack={diagnostic.OutputTrack}, outputTick={diagnostic.OutputTick}, count={diagnostic.OccurrenceCount}) " +
                $"{diagnostic.Original} -> {diagnostic.Converted}");
        }
    }
}
