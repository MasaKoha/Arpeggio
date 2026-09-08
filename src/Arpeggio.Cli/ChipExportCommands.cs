using System.CommandLine;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;

namespace Arpeggio.Cli
{
    /// <summary>NSF／VGM の事前診断とファイル書き出しコマンドを構成する。</summary>
    internal static class ChipExportCommands
    {
        private const int DefaultLoops = 1;

        internal static Command Create(string name)
        {
            ConversionFormat format = name == "nsf" ? ConversionFormat.Nsf : ConversionFormat.Vgm;
            var command = new Command(name, $"{name.ToUpperInvariant()} のチップ演奏を保存");
            var source = new Argument<string>("song");
            var output = new Argument<string>("output");
            var loops = new Option<int>("--loops") { DefaultValueFactory = _ => DefaultLoops };
            var author = new Option<string>("--author") { DefaultValueFactory = _ => string.Empty };
            var copyright = new Option<string>("--copyright") { DefaultValueFactory = _ => string.Empty };
            var strict = new Option<bool>("--strict");
            var dryRun = new Option<bool>("--dry-run");
            var overwrite = new Option<bool>("--overwrite");
            var json = new Option<bool>("--json");
            command.Arguments.Add(source);
            command.Arguments.Add(output);
            command.Options.Add(loops);
            command.Options.Add(author);
            command.Options.Add(strict);
            command.Options.Add(dryRun);
            command.Options.Add(overwrite);
            command.Options.Add(json);
            if (format == ConversionFormat.Nsf)
            {
                command.Options.Add(copyright);
            }
            command.SetAction(result =>
            {
                var execution = new CliConversionExecution(new ConversionReport(format, ChipKind.None, result.GetValue(strict)),
                    result.GetValue(output)!, result.GetValue(json), result.GetValue(dryRun));
                return execution.Run(() =>
                {
                    string sourcePath = result.GetValue(source)!;
                    Song song = SongSerializer.Load(sourcePath);
                    execution.Report = new ConversionReport(format, song.Chip, result.GetValue(strict));
                    var options = new ChipExportOptions
                    {
                        Format = format, Loops = result.GetValue(loops), Author = result.GetValue(author)!,
                        Copyright = format == ConversionFormat.Nsf ? result.GetValue(copyright)! : string.Empty,
                        Strict = result.GetValue(strict)
                    };
                    ChipExportPlan plan = ChipExportService.Prepare(song, options);
                    execution.Report = plan.Report;
                    execution.ValidateDestination(sourcePath);
                    if (!execution.DryRun)
                    {
                        execution.Written = ChipExportService.Write(plan, execution.Path, result.GetValue(overwrite), sourcePath: sourcePath);
                    }
                });
            });
            return command;
        }
    }
}
