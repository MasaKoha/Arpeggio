using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Formats.Midi;
using Arpeggio.Core.Tests.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import;
using Arpeggio.Daw.Presenters.Midi;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>MIDI 入出力と既存文書を同じ隔離ディレクトリに所有する。</summary>
    internal sealed class MidiImportDawFixture : IDisposable
    {
        internal MidiImportDawFixture()
        {
            SourcePath = Path.ChangeExtension(Editor.Path, ".mid");
            DestinationPath = Path.Combine(DirectoryPath, "imported.arpeggio.json");
            File.WriteAllBytes(SourcePath, MidiImporterTests.SimpleMidi());
        }

        internal DawPresenterFixture Editor { get; } = new DawPresenterFixture();
        internal MidiImportPresenter Import => Editor.Presenter.MidiImport;
        internal string SourcePath { get; }
        internal string DestinationPath { get; }
        internal string DirectoryPath => Path.GetDirectoryName(Editor.Path)!;

        internal MidiImportInput Input(ChipKind chip = ChipKind.Nes, bool strict = false) => new MidiImportInput
        {
            SourcePath = SourcePath, DestinationPath = DestinationPath, Chip = chip, Strict = strict
        };

        internal void CreateUndoAndRedo()
        {
            Editor.Presenter.PianoRoll.Add(0, 60);
            Editor.Presenter.PianoRoll.Add(48, 64);
            Editor.Presenter.Undo();
        }

        internal MidiImportResult RequireCandidate() => Import.PreparedResult
            ?? throw new InvalidOperationException("候補がありません。");

        /// <summary>Presenter と MIDI 入出力をすべて解放する。</summary>
        public void Dispose() => Editor.Dispose();
    }
}
