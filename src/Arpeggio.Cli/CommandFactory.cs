using System.CommandLine;

namespace Arpeggio.Cli
{
    /// <summary>arpeggio の独立したコマンドツリーを組み立てる。</summary>
    public static class CommandFactory
    {
        /// <summary>呼び出し間で Option を共有せずルートを生成する。</summary>
        public static RootCommand Create()
        {
            RootCommand root = new RootCommand("AI と人のためのチップチューン編集ツール");
            root.Subcommands.Add(SongCommands.CreateNew());
            root.Subcommands.Add(SongCommands.CreateInfo());
            root.Subcommands.Add(SongCommands.CreateShow());
            root.Subcommands.Add(NoteCommands.Create());
            root.Subcommands.Add(InstrumentCommands.Create());
            root.Subcommands.Add(BatchCommands.Create());
            root.Subcommands.Add(SongCommands.CreateExport());
            root.Subcommands.Add(MidiImportCommands.Create());
            root.Subcommands.Add(AnalysisCommands.Create());
            root.Subcommands.Add(SfxCommands.Create());
            root.Subcommands.Add(SongCommands.CreateChipReference());
            root.Subcommands.Add(SongCommands.CreateHistory("undo"));
            root.Subcommands.Add(SongCommands.CreateHistory("redo"));
            return root;
        }
    }
}
