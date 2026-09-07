using System;
using System.CommandLine;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Cli
{
    /// <summary>編集可能な効果音の雛形生成とカタログ表示。</summary>
    internal static class SfxCommands
    {
        internal static Command Create()
        {
            Command command = new Command("sfx", "効果音プリセット");
            command.Subcommands.Add(CreateNew());
            command.Subcommands.Add(CreateList());
            return command;
        }

        private static Command CreateNew()
        {
            Command command = new Command("new", "短いソングとして効果音を生成");
            Argument<string> path = new Argument<string>("path");
            Option<string> preset = new Option<string>("--preset") { Required = true };
            Option<string> chip = new Option<string>("--chip") { DefaultValueFactory = _ => "nes" };
            Option<string?> title = new Option<string?>("--title");
            command.Arguments.Add(path);
            command.Options.Add(preset);
            command.Options.Add(chip);
            command.Options.Add(title);
            command.SetAction(result => CliExecution.Run(() =>
            {
                string outputPath = result.GetValue(path)!;
                SfxPresetFile.Create(outputPath, ChipReference.ParseChip(result.GetValue(chip)!),
                    SfxPresetCatalog.Parse(result.GetValue(preset)!), result.GetValue(title));
                EditSession session = CliExecution.Open(outputPath);
                CliHistoryStore.Save(session);
                Console.WriteLine(session.Path);
                return CliExecution.Success;
            }));
            return command;
        }

        private static Command CreateList()
        {
            Command command = new Command("list", "全プリセットの名前と説明");
            command.SetAction(_ => CliExecution.Run(() =>
            {
                foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
                {
                    Console.WriteLine($"{preset.Name}: {preset.Description}");
                }
                return CliExecution.Success;
            }));
            return command;
        }
    }
}
