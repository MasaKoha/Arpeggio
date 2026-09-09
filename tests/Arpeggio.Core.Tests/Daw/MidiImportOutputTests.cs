using System;
using System.IO;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>DAW で保存した MIDI 候補を明示 Open した後、同じ曲を各形式へ書き出す統合境界を検証する。</summary>
    public sealed class MidiImportOutputTests
    {
        private const int SampleRate = 44100;
        private const int SongFrames = 66150;
        private const int AudioFramesWithDefaultTail = 88200;

        /// <summary>三チップの Open による切替が既存音声とチップ書き出しの双方へ反映される。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public async Task SavedMidiOpensAndExportsCurrentCandidate(ChipKind chip)
        {
            using var fixture = new MidiImportDawFixture();
            File.WriteAllBytes(fixture.SourcePath, MidiPipelineFixture.CreateMidi());
            await fixture.Import.PrepareAsync(fixture.Input(chip));
            string expected = fixture.RequireCandidate().Json!;
            await fixture.Import.SaveAsync();
            fixture.Import.OpenSaved();
            Assert.Equal(chip, fixture.Editor.Document.Song.Chip);
            Assert.Equal(expected, SongSerializer.Serialize(fixture.Editor.Document.Song));

            string wavePath = Path.Combine(fixture.DirectoryPath, "daw.wav");
            await fixture.Editor.Presenter.Export.RunAsync(wavePath);
            Assert.Equal(wavePath, fixture.Editor.Presenter.Export.LastExportedPath);
            float[] samples = WavReader.Read(wavePath, out int rate);
            Assert.Equal(SampleRate, rate);
            Assert.Contains(samples, sample => Math.Abs(sample) > 0.001f);
            string oggPath = Path.Combine(fixture.DirectoryPath, "daw.ogg");
            await fixture.Editor.Presenter.Export.RunAsync(oggPath);
            Assert.Equal(oggPath, fixture.Editor.Presenter.Export.LastExportedPath);
            MidiPipelineTests.AssertOggEndOfStream(File.ReadAllBytes(oggPath), AudioFramesWithDefaultTail);
            if (chip != ChipKind.Snes) { await ExportChipAsync(fixture, ConversionFormat.Vgm); }
            if (chip == ChipKind.Nes) { await ExportChipAsync(fixture, ConversionFormat.Nsf); }
            Assert.Equal(expected, File.ReadAllText(fixture.DestinationPath));
            Assert.False(fixture.Editor.Presenter.IsDirty);
            Assert.Equal(0, fixture.Editor.Document.Session.History.UndoCount);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        private static async Task ExportChipAsync(MidiImportDawFixture fixture, ConversionFormat format)
        {
            string path = Path.Combine(fixture.DirectoryPath, format == ConversionFormat.Nsf ? "daw.nsf" : "daw.vgm");
            var export = fixture.Editor.Presenter.Export;
            export.SelectChipDestination(path);
            await export.PrepareChipAsync(new ChipExportOptions { Format = format });
            Assert.True(export.CanSaveChip);
            await export.SaveChipAsync();
            Assert.Equal(path, export.LastExportedPath);
            if (format == ConversionFormat.Vgm)
            {
                Assert.Equal(SongFrames, IndependentVgmParser.Parse(File.ReadAllBytes(path)).WaitSamples);
            }
            else
            {
                Assert.Equal("song.arpeggio", IndependentNsfLoader.Load(path).Title);
            }
        }
    }
}
