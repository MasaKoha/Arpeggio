using System;
using System.CommandLine;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>音色の一覧・追加・部分更新・削除の CLI 定義。</summary>
    internal static class InstrumentCommands
    {
        internal static Command Create()
        {
            Command command = new("instrument", "チップ固有の音色を管理");
            command.Subcommands.Add(CreateList());
            command.Subcommands.Add(CreateAdd());
            command.Subcommands.Add(CreateSet());
            command.Subcommands.Add(CreateRemove());
            return command;
        }

        private static Command CreateList()
        {
            Command command = new("list", "音色一覧");
            Argument<string> path = new("path");
            Option<bool> json = new("--json") { Description = "音色 JSON 配列" };
            command.Arguments.Add(path);
            command.Options.Add(json);
            command.SetAction(result => CliExecution.Run(() =>
            {
                EditSession session = CliExecution.Open(result.GetValue(path)!);
                Song song = session.Song!;
                if (result.GetValue(json))
                {
                    Console.WriteLine("[" + string.Join(",", song.Instruments.Select(InstrumentJson.Serialize)) + "]");
                    return CliExecution.Success;
                }
                foreach (Instrument instrument in song.Instruments)
                {
                    Console.WriteLine($"{instrument.Id:D2} {instrument.Kind} {instrument.Name}");
                }
                return CliExecution.Success;
            }));
            return command;
        }

        private static Command CreateAdd()
        {
            Command command = new("add", "音色を追加し、自動採番した ID を返す");
            Argument<string> path = new("path");
            InstrumentOptions options = new(command);
            options.RequireNewInstrument();
            command.Arguments.Add(path);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(path)!, session =>
            {
                Instrument instrument = InstrumentOptions.Create(result.GetValue(options.Kind)!.Value);
                Song song = session.Song!;
                instrument.Id = checked(song.Instruments.Select(existing => existing.Id).DefaultIfEmpty(0).Max() + 1);
                instrument = options.Apply(result, instrument);
                session.Instruments.Add(instrument);
                Console.WriteLine(instrument.Id);
            })));
            return command;
        }

        private static Command CreateSet()
        {
            Command command = new("set", "指定した音色パラメータだけを更新");
            Argument<string> path = new("path");
            Option<int> identifier = new("--id") { Required = true, Description = "音色 ID" };
            InstrumentOptions options = new(command);
            command.Arguments.Add(path);
            command.Options.Add(identifier);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(path)!, session =>
            {
                int instrumentId = result.GetValue(identifier);
                Instrument existing = session.Song!.Instruments.Find(instrument => instrument.Id == instrumentId)
                    ?? throw new ArgumentException($"音色 ID {instrumentId} が存在しません。");
                InstrumentKind kind = result.GetValue(options.Kind) ?? existing.Kind;
                Instrument replacement = options.Apply(result, InstrumentOptions.ChangeKind(existing, kind));
                session.Instruments.Update(replacement);
                Console.WriteLine(replacement.Id);
            })));
            return command;
        }

        private static Command CreateRemove()
        {
            Command command = new("remove", "未使用の音色を削除");
            Argument<string> path = new("path");
            Option<int> identifier = new("--id") { Required = true, Description = "音色 ID" };
            command.Arguments.Add(path);
            command.Options.Add(identifier);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(path)!, session =>
                session.Instruments.Remove(result.GetValue(identifier)))));
            return command;
        }
    }
}
