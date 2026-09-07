using System;
using System.CommandLine;
using System.Globalization;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>ノートの追加・削除・移動・長さ変更コマンド。</summary>
    internal static class NoteCommands
    {
        private const int DefaultVolume = 15;

        internal static Command Create()
        {
            Command command = new Command("note", "ノートを編集");
            command.Subcommands.Add(CreateAdd());
            command.Subcommands.Add(CreateRemove());
            command.Subcommands.Add(CreateMove());
            command.Subcommands.Add(CreateResize());
            return command;
        }

        private static Command CreateAdd()
        {
            Command command = new Command("add", "ノートを追加");
            NoteTargetOptions target = new NoteTargetOptions(command);
            Option<int> duration = new Option<int>("--duration") { Required = true };
            Option<string> note = new Option<string>("--note") { Required = true, Description = "C5 / C#5 / Db5 / MIDI 番号（60 = C4）" };
            Option<int> volume = new Option<int>("--volume") { DefaultValueFactory = _ => DefaultVolume };
            Option<int?> instrument = new Option<int?>("--instrument") { Description = "音色 ID。省略時はトラックのチャンネルに合う最初の音色" };
            Option<string[]> effects = new Option<string[]>("--effect")
            {
                AllowMultipleArgumentsPerToken = true,
                Arity = ArgumentArity.OneOrMore,
                Description = "Kind=Value を複数指定。例: PitchSlide=-4 Vibrato=20"
            };
            command.Options.Add(duration);
            command.Options.Add(note);
            command.Options.Add(volume);
            command.Options.Add(instrument);
            command.Options.Add(effects);
            command.SetAction(result => CliExecution.Run(() =>
            {
                Note added = new Note
                {
                    Tick = result.GetValue(target.Tick), DurationTicks = result.GetValue(duration),
                    MidiNote = NoteName.Parse(result.GetValue(note)!), Volume = result.GetValue(volume),
                    Effects = (result.GetValue(effects) ?? Array.Empty<string>()).Select(ParseEffect).ToArray()
                };
                int trackIndex = result.GetValue(target.Track);
                int? instrumentId = result.GetValue(instrument);
                return CliExecution.Edit(result.GetValue(target.Path)!, session =>
                {
                    added.InstrumentId = DefaultInstrumentResolver.Resolve(session.Song!, trackIndex, instrumentId);
                    session.Notes.Add(trackIndex, added);
                });
            }));
            return command;
        }

        private static Command CreateRemove()
        {
            Command command = new Command("remove", "開始 tick でノートを削除");
            NoteTargetOptions target = new NoteTargetOptions(command);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(target.Path)!, session =>
                session.Notes.Remove(result.GetValue(target.Track), result.GetValue(target.Tick)))));
            return command;
        }

        private static Command CreateMove()
        {
            Command command = new Command("move", "ノートの開始 tick と音高を変更");
            NoteTargetOptions target = new NoteTargetOptions(command);
            Option<int> toTick = new Option<int>("--to-tick") { Required = true };
            Option<string?> note = new Option<string?>("--note");
            command.Options.Add(toTick);
            command.Options.Add(note);
            command.SetAction(result => CliExecution.Run(() =>
            {
                string? noteName = result.GetValue(note);
                int? midiNote = noteName == null ? null : NoteName.Parse(noteName);
                return CliExecution.Edit(result.GetValue(target.Path)!, session =>
                    session.Notes.Move(result.GetValue(target.Track), result.GetValue(target.Tick), result.GetValue(toTick), midiNote));
            }));
            return command;
        }

        private static Command CreateResize()
        {
            Command command = new Command("resize", "ノートの長さを変更");
            NoteTargetOptions target = new NoteTargetOptions(command);
            Option<int> duration = new Option<int>("--duration") { Required = true };
            command.Options.Add(duration);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(target.Path)!, session =>
                session.Notes.Resize(result.GetValue(target.Track), result.GetValue(target.Tick), result.GetValue(duration)))));
            return command;
        }

        private static NoteEffect ParseEffect(string text)
        {
            string[] parts = text.Split('=');
            if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out NoteEffectKind kind) ||
                kind == NoteEffectKind.None || !Enum.IsDefined(kind))
            {
                throw new ArgumentException($"エフェクトは Kind=Value で指定してください: {text}");
            }
            string value = parts[1];
            int amount = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return new NoteEffect(kind, amount);
        }
    }
}
