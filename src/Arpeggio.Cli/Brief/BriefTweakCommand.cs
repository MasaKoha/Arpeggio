using System.CommandLine;
using Arpeggio.Core.Brief;

namespace Arpeggio.Cli.Brief
{
    /// <summary>指定項目をまとめて検証し、作曲指示書を一度だけ保存する。</summary>
    internal static class BriefTweakCommand
    {
        internal static Command Create()
        {
            var command = new Command("tweak", "作曲指示書を部分編集");
            Argument<string> path = BriefParameterOptions.AddPath(command);
            var parameters = new BriefParameterOptions(command);
            var json = new Option<bool>("--json");
            command.Options.Add(json);
            command.SetAction(result => CliBriefExecution.Run("tweak", result.GetValue(json), () =>
            {
                string destination = result.GetValue(path)!;
                CompositionBrief current = CompositionBriefFile.Load(destination);
                CompositionBrief candidate = parameters.Apply(result, current);
                CompositionBriefFile.Save(candidate, destination);
                return CliBriefExecution.Saved("tweak", destination, candidate, result.GetValue(json));
            }));
            return command;
        }
    }
}
