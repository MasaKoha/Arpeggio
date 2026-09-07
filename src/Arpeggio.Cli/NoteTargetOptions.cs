using System.CommandLine;

namespace Arpeggio.Cli
{
    /// <summary>ノート編集で共通の保存先と検索位置を束ねる。</summary>
    internal sealed class NoteTargetOptions
    {
        internal NoteTargetOptions(Command command)
        {
            command.Arguments.Add(Path);
            command.Options.Add(Track);
            command.Options.Add(Tick);
        }

        internal Argument<string> Path { get; } = new Argument<string>("path");
        internal Option<int> Track { get; } = new Option<int>("--track") { Required = true, Description = "0 始まりのトラック" };
        internal Option<int> Tick { get; } = new Option<int>("--tick") { Required = true, Description = "ノート開始 tick" };
    }
}
