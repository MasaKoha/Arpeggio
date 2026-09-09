using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Midi;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>テンポ変更・三和音・打楽器を持つ MIDI と全出力の保存先を隔離する。</summary>
    internal sealed class MidiPipelineFixture : IDisposable
    {
        internal MidiPipelineFixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllBytes(SourcePath, CreateMidi());
        }

        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "arpeggio-m3-" + Guid.NewGuid().ToString("N"));
        internal string SourcePath => PathFor("input.mid");
        internal string SongPath => PathFor("imported.arpeggio.json");
        internal string PathFor(string name) => Path.Combine(DirectoryPath, name);

        internal MidiImportResult Import(ChipKind chip)
        {
            using FileStream stream = File.OpenRead(SourcePath);
            MidiImportResult result = MidiImporter.Import(stream, new MidiImportOptions { Chip = chip, SourceName = SourcePath });
            MidiSongFile.Write(result, SongPath, sourcePath: SourcePath);
            return result;
        }

        internal static byte[] CreateMidi() => MidiFileFixture.Create(
            MidiFileFixture.Bytes("00 FF 51 03 07 A1 20 83 60 FF 51 03 0F 42 40 83 60 FF 2F 00"),
            MidiFileFixture.Bytes("00 C0 28 00 90 3C 7F 00 90 40 7F 00 90 43 7F 00 99 24 7F 01 89 24 00 " +
                "83 5F 80 3C 00 00 80 40 00 00 80 43 00 00 90 45 7F 83 60 80 45 00 00 FF 2F 00"));

        /// <summary>このテストが所有する全入出力を削除する。</summary>
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
