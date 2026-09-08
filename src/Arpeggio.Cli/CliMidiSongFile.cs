using System.IO;
using Arpeggio.Core.Session;
using Arpeggio.Formats.Midi;

namespace Arpeggio.Cli
{
    /// <summary>MIDI の新規保存と、新しい文書だけに属する空の CLI 履歴を確定する。</summary>
    internal static class CliMidiSongFile
    {
        internal static bool Write(MidiImportResult result, string path, string sourcePath, bool dryRun)
        {
            MidiSongFileResult saved = MidiSongFile.Write(result, path, dryRun, sourcePath: sourcePath);
            if (!saved.Written)
            {
                return false;
            }
            try
            {
                // 同じ JSON が以前存在していても、残存側車の undo / redo を新規文書へ継承しない。
                EditSession session = CliExecution.Open(path);
                CliHistoryStore.Save(session);
                return true;
            }
            catch
            {
                // 新規作成だけが許可されているため、履歴を確定できない出力は取り消せる。
                File.Delete(path);
                throw;
            }
        }
    }
}
