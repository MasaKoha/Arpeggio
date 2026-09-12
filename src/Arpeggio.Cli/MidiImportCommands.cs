using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Arpeggio.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Cli
{
    /// <summary>SMF の全変換・事前診断と、新規 Song 保存コマンドを構成する。</summary>
    internal static class MidiImportCommands
    {
        private const int DefaultQuantizeTicks = 1;

        internal static Command Create()
        {
            var command = new Command("import", "外部ファイルを新規ソングへ取り込む");
            command.Subcommands.Add(CreateMidi());
            return command;
        }

        private static Command CreateMidi()
        {
            var command = new Command("midi", "SMF format 0 / 1 を新規ソングへ取り込む");
            var source = new Argument<string>("input");
            var output = new Argument<string>("output");
            var chip = new Option<string>("--chip") { Required = true, Description = "nes / gameboy / snes" };
            var tempo = new Option<int?>("--tempo");
            var quantizeTicks = new Option<int>("--quantize-ticks") { DefaultValueFactory = _ => DefaultQuantizeTicks };
            var polyphony = new Option<string>("--polyphony") { DefaultValueFactory = _ => "steal-oldest", Description = "steal-oldest / drop-new" };
            var channelMap = new Option<string?>("--channel-map");
            var title = new Option<string?>("--title");
            var strict = new Option<bool>("--strict");
            var dryRun = new Option<bool>("--dry-run");
            var json = new Option<bool>("--json");
            command.Arguments.Add(source);
            command.Arguments.Add(output);
            command.Options.Add(chip);
            command.Options.Add(tempo);
            command.Options.Add(quantizeTicks);
            command.Options.Add(polyphony);
            command.Options.Add(channelMap);
            command.Options.Add(title);
            command.Options.Add(strict);
            command.Options.Add(dryRun);
            command.Options.Add(json);
            command.SetAction(result =>
            {
                var execution = new CliConversionExecution(new ConversionReport(ConversionFormat.Midi, ChipKind.None, result.GetValue(strict)),
                    result.GetValue(output)!, result.GetValue(json), result.GetValue(dryRun));
                return execution.Run(() =>
                {
                    ChipKind selectedChip = ChipReference.ParseChip(result.GetValue(chip)!);
                    execution.Report = new ConversionReport(ConversionFormat.Midi, selectedChip, result.GetValue(strict));
                    IReadOnlyDictionary<int, IReadOnlyList<int>>? candidates = MidiChannelMapFile.Read(result.GetValue(channelMap), execution.Report);
                    if (execution.Report.ErrorCount != 0)
                    {
                        return;
                    }
                    string sourcePath = result.GetValue(source)!;
                    var options = new MidiImportOptions
                    {
                        Chip = selectedChip, Tempo = result.GetValue(tempo), QuantizeTicks = result.GetValue(quantizeTicks),
                        Polyphony = ParsePolyphony(result.GetValue(polyphony)!), ChannelMap = candidates,
                        Title = result.GetValue(title), SourceName = sourcePath, Strict = result.GetValue(strict)
                    };
                    MidiImportResult imported;
                    using (FileStream stream = File.OpenRead(sourcePath))
                    {
                        imported = MidiImporter.Import(stream, options);
                    }
                    execution.Report = imported.Report;
                    execution.ValidateDestination(sourcePath);
                    execution.Written = CliMidiSongFile.Write(imported, execution.Path, sourcePath, execution.DryRun);
                });
            });
            return command;
        }

        private static MidiPolyphonyMode ParsePolyphony(string text)
        {
            return text switch
            {
                "steal-oldest" => MidiPolyphonyMode.StealOldest,
                "drop-new" => MidiPolyphonyMode.DropNew,
                _ => MidiPolyphonyMode.None
            };
        }
    }
}
