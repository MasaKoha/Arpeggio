using System;
using System.CommandLine;
using System.IO;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>JSON 配列による一括編集コマンドを構成する。</summary>
    internal static class BatchCommands
    {
        internal static Command Create()
        {
            Command command = new Command("apply", "JSON 操作配列を一履歴として適用");
            Argument<string> path = new Argument<string>("path");
            Option<string> operations = new Option<string>("--operations") { Required = true, Description = "JSON ファイル。- は標準入力" };
            Option<int?> track = new Option<int?>("--track") { Description = "track を省略した操作の既定トラック" };
            command.Arguments.Add(path);
            command.Options.Add(operations);
            command.Options.Add(track);
            command.SetAction(result => CliExecution.Run(() =>
            {
                string input = result.GetValue(operations)!;
                string json = input == "-" ? Console.In.ReadToEnd() : File.ReadAllText(input);
                BatchOperation[] parsed = BatchOperationJson.Deserialize(json);
                return CliExecution.Edit(result.GetValue(path)!, session =>
                {
                    BatchOperationApplier.Apply(session, parsed, result.GetValue(track));
                    Console.WriteLine(SessionOutput.Serialize(new { applied = parsed.Length }));
                });
            }));
            return command;
        }
    }
}
