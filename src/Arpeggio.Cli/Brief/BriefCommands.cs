using System.CommandLine;
using Arpeggio.Core.Brief;

namespace Arpeggio.Cli.Brief
{
    /// <summary>作曲指示書の作成・編集・表示をルートへ登録する。</summary>
    internal static class BriefCommands
    {
        internal static Command Create()
        {
            var command = new Command("brief", "AI へ渡す作曲指示書");
            command.Subcommands.Add(BriefCreateCommand.Create());
            command.Subcommands.Add(BriefTweakCommand.Create());
            command.Subcommands.Add(CreateRead("show", "作曲指示書をテキストまたは JSON で表示"));
            command.Subcommands.Add(CreateRead("text", "指示書テキストだけを出力"));
            return command;
        }

        private static Command CreateRead(string operation, string description)
        {
            var command = new Command(operation, description);
            Argument<string> path = BriefParameterOptions.AddPath(command);
            var json = new Option<bool>("--json")
            {
                Description = operation == "text" ? "失敗時の応答を JSON にする" : "JSON 形式で出力する"
            };
            command.Options.Add(json);
            command.SetAction(result => CliBriefExecution.Run(operation, result.GetValue(json), () =>
            {
                CompositionBrief brief = CompositionBriefFile.Load(result.GetValue(path)!);
                return operation == "show" && result.GetValue(json)
                    ? CompositionBriefFile.Serialize(brief) : CompositionBriefTextRenderer.Render(brief);
            }));
            return command;
        }
    }
}
