using System;
using System.CommandLine;
using Arpeggio.Cli.Sfx;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;

namespace Arpeggio.Cli.Brief
{
    /// <summary>作曲指示書の値オプションを保護し、指定項目だけを更新する。</summary>
    internal sealed class BriefParameterOptions
    {
        private readonly Option<string?> _title;
        private readonly Option<string?> _chip;
        private readonly Option<int?> _tempo;
        private readonly Option<string?> _mood;
        private readonly Option<string?> _structure;
        private readonly Option<string?> _instrumentation;
        private readonly Option<string?> _references;
        private readonly Option<string?> _constraints;
        private readonly Option<string?> _notes;
        private readonly Option<bool> _clearChip = new Option<bool>("--clear-chip");
        private readonly Option<bool> _clearTempo = new Option<bool>("--clear-tempo");

        internal BriefParameterOptions(Command command)
        {
            _title = AddValue<string?>(command, "--title");
            _chip = AddValue<string?>(command, "--chip");
            _tempo = AddValue<int?>(command, "--tempo");
            _mood = AddValue<string?>(command, "--mood");
            _structure = AddValue<string?>(command, "--structure");
            _instrumentation = AddValue<string?>(command, "--instrumentation");
            _references = AddValue<string?>(command, "--references");
            _constraints = AddValue<string?>(command, "--constraints");
            _notes = AddValue<string?>(command, "--notes");
            command.Options.Add(_clearChip);
            command.Options.Add(_clearTempo);
        }

        internal CompositionBrief Apply(ParseResult result, CompositionBrief current)
        {
            bool clearChip = result.GetValue(_clearChip);
            bool clearTempo = result.GetValue(_clearTempo);
            if (clearChip && result.GetResult(_chip) != null)
            {
                throw new CompositionBriefException("InvalidParameter", "chip", "--chip と --clear-chip は併用できません。");
            }
            if (clearTempo && result.GetResult(_tempo) != null)
            {
                throw new CompositionBriefException("InvalidParameter", "tempoBpm", "--tempo と --clear-tempo は併用できません。");
            }
            RequireChanges(result, clearChip || clearTempo);
            string? chip = result.GetValue(_chip);
            ChipKind? selectedChip = chip == null ? current.Chip : ParseChip(chip);
            return current with
            {
                Title = result.GetValue(_title) ?? current.Title,
                Chip = clearChip ? null : selectedChip,
                TempoBpm = clearTempo ? null : result.GetValue(_tempo) ?? current.TempoBpm,
                Mood = result.GetValue(_mood) ?? current.Mood,
                Structure = result.GetValue(_structure) ?? current.Structure,
                Instrumentation = result.GetValue(_instrumentation) ?? current.Instrumentation,
                References = result.GetValue(_references) ?? current.References,
                Constraints = result.GetValue(_constraints) ?? current.Constraints,
                Notes = result.GetValue(_notes) ?? current.Notes
            };
        }

        internal static Argument<string> AddPath(Command command)
        {
            var path = new Argument<string>("path") { Arity = ArgumentArity.ExactlyOne };
            path.Validators.Add(CliArgumentGuard.RejectFlagLikeToken);
            command.Arguments.Add(path);
            return path;
        }

        internal static Option<T> AddValue<T>(Command command, string name)
        {
            var option = new Option<T>(name) { Arity = ArgumentArity.ExactlyOne };
            option.Validators.Add(CliArgumentGuard.RejectFlagLikeToken);
            option.Validators.Add(result =>
            {
                if (result.IdentifierTokenCount > 1)
                {
                    result.AddError($"{name} は重複して指定できません。");
                }
            });
            command.Options.Add(option);
            return option;
        }

        internal static ChipKind ParseChip(string value)
        {
            try
            {
                return ChipReference.ParseChip(value);
            }
            catch (ArgumentException exception)
            {
                throw new CompositionBriefException("InvalidParameter", "chip", exception.Message, exception);
            }
        }

        private void RequireChanges(ParseResult result, bool hasClear)
        {
            Option[] values = { _title, _chip, _tempo, _mood, _structure, _instrumentation, _references, _constraints, _notes };
            foreach (Option option in values)
            {
                if (result.GetResult(option) != null)
                {
                    return;
                }
            }
            if (!hasClear)
            {
                throw new CompositionBriefException("InvalidParameter", "arguments", "変更する項目を指定してください。");
            }
        }
    }
}
