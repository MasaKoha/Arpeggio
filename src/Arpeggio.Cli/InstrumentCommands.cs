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
            command.Subcommands.Add(CreateImportWav());
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
                    Console.WriteLine("[" + string.Join(",", song.Instruments.Select(InstrumentJson.SerializeForDisplay)) + "]");
                    return CliExecution.Success;
                }
                foreach (Instrument instrument in song.Instruments)
                {
                    string sampleDescription = instrument is SnesSampleInstrument sample && sample.SampleData != null
                        ? $" | {sample.SampleSummary}" : string.Empty;
                    Console.WriteLine($"{instrument.Id:D2} {instrument.Kind} {instrument.Name}{sampleDescription}");
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

        private static Command CreateImportWav()
        {
            Command command = new Command("import-wav", "WAV を既存 SNES 音色へ埋め込む");
            Argument<string> path = new Argument<string>("path");
            Argument<string> wavPath = new Argument<string>("wavPath");
            Option<int> identifier = new Option<int>("--id") { Required = true, Description = "SNES 音色 ID" };
            Option<string> root = new Option<string>("--root") { DefaultValueFactory = _ => "C4" };
            Option<int?> loopStart = new Option<int?>("--loop-start") { Description = "開始サンプル（含む）" };
            Option<int?> loopEnd = new Option<int?>("--loop-end") { Description = "終端サンプル（含まない）。0 は末尾" };
            Option<bool> noLoop = new Option<bool>("--no-loop");
            command.Arguments.Add(path);
            command.Arguments.Add(wavPath);
            command.Options.Add(identifier);
            command.Options.Add(root);
            command.Options.Add(loopStart);
            command.Options.Add(loopEnd);
            command.Options.Add(noLoop);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(path)!, session =>
            {
                session.Instruments.ImportWavSample(result.GetValue(identifier), result.GetValue(wavPath)!,
                    NoteName.Parse(result.GetValue(root)!), result.GetValue(loopStart), result.GetValue(loopEnd), !result.GetValue(noLoop));
                Console.WriteLine(result.GetValue(identifier));
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
