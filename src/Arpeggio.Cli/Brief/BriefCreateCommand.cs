using System.CommandLine;
using Arpeggio.Core.Brief;

namespace Arpeggio.Cli.Brief
{
    /// <summary>作曲指示書の空テンプレートを新規保存する。</summary>
    internal static class BriefCreateCommand
    {
        internal static Command Create()
        {
            var command = new Command("create", "作曲指示書を新規保存");
            Argument<string> path = BriefParameterOptions.AddPath(command);
            Option<string?> title = BriefParameterOptions.AddValue<string?>(command, "--title");
            Option<string?> chip = BriefParameterOptions.AddValue<string?>(command, "--chip");
            var json = new Option<bool>("--json");
            command.Options.Add(json);
            command.SetAction(result => CliBriefExecution.Run("create", result.GetValue(json), () =>
            {
                string destination = result.GetValue(path)!;
                string? selectedChip = result.GetValue(chip);
                var brief = new CompositionBrief
                {
                    Title = result.GetValue(title) ?? CompositionBrief.DefaultTitle,
                    Chip = selectedChip == null ? null : BriefParameterOptions.ParseChip(selectedChip)
                };
                CompositionBriefFile.Create(brief, destination);
                return CliBriefExecution.Saved("create", destination, brief, result.GetValue(json));
            }));
            return command;
        }
    }
}
