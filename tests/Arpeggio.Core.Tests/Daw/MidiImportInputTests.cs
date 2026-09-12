using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Core.Tests.Formats.Midi;
using Arpeggio.Core.Tests.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import.Voice;
using Arpeggio.Daw.Presenters.Midi;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>画面入力のエラー・map の厳密性と共通変換への設定引き渡しを検証する。</summary>
    public sealed class MidiImportInputTests
    {
        /// <summary>基準テンポ・量子化・map・polyphony・title は共通 Import と同じ候補と統計を返す。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest, 1, 0)]
        [InlineData(MidiPolyphonyMode.DropNew, 0, 1)]
        public async Task OptionsAndMapMatchCommonImporter(MidiPolyphonyMode mode, long truncated, long dropped)
        {
            using var fixture = new MidiImportDawFixture();
            byte[] midi = MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 90 3C 7F 81 70 90 40 7F 81 70 80 3C 00 00 80 40 00 00 FF 2F 00"));
            File.WriteAllBytes(fixture.SourcePath, midi);
            string mapPath = Path.Combine(fixture.DirectoryPath, "map.json");
            File.WriteAllText(mapPath, "{\"1\":[1]}", new UTF8Encoding(true));
            var input = new MidiImportInput
            {
                SourcePath = fixture.SourcePath, DestinationPath = fixture.DestinationPath, Chip = ChipKind.Nes,
                Tempo = "100", QuantizeTicks = "12", Polyphony = mode, ChannelMapPath = mapPath, Title = "取り込み"
            };
            await fixture.Import.PrepareAsync(input);
            MidiImportResult expected = MidiImporterTests.Import(midi, new MidiImportOptions
            {
                Chip = ChipKind.Nes, Tempo = 100, QuantizeTicks = 12, Polyphony = mode, Title = "取り込み",
                SourceName = fixture.SourcePath, ChannelMap = new Dictionary<int, IReadOnlyList<int>> { [1] = new[] { 1 } }
            });
            MidiImportResult actual = fixture.RequireCandidate();
            Assert.Equal(expected.Json, actual.Json);
            Assert.Equal(truncated, actual.Report.Statistics["truncatedMidiNotes"]);
            Assert.Equal(dropped, actual.Report.Statistics["polyphonyNotesDropped"]);
            Assert.Contains("ch 1 → track 1", fixture.Import.ReportText);
            await fixture.Import.SaveAsync();
            Assert.Equal(expected.Json, File.ReadAllText(fixture.DestinationPath));
        }

        /// <summary>文字列解析・整数の範囲・グリッドの拒否では旧候補を保存できない。</summary>
        [Theory]
        [InlineData("invalid", "1")]
        [InlineData("2147483648", "1")]
        [InlineData("0", "1")]
        [InlineData("1001", "1")]
        [InlineData("", "invalid")]
        [InlineData("", "0")]
        [InlineData("", "5")]
        public async Task InvalidNumericInputCannotSavePreviousCandidate(string tempo, string quantize)
        {
            using var fixture = new MidiImportDawFixture();
            await fixture.Import.PrepareAsync(fixture.Input());
            await fixture.Import.PrepareAsync(new MidiImportInput
            {
                SourcePath = fixture.SourcePath, DestinationPath = fixture.DestinationPath,
                Chip = ChipKind.Nes, Tempo = tempo, QuantizeTicks = quantize
            });
            Assert.False(fixture.Import.CanSave);
            Assert.False(fixture.Import.CanOpen);
            await fixture.Import.SaveAsync();
            Assert.False(File.Exists(fixture.DestinationPath));
            Assert.Equal(fixture.Editor.Path, fixture.Editor.Document.Path);
            Assert.False(fixture.Editor.Presenter.IsDirty);
        }

        /// <summary>構造不正・重複・範囲・互換性を失わず map を拒否する。</summary>
        [Theory]
        [InlineData("[]")]
        [InlineData("{\"1\":[0],\"01\":[1]}")]
        [InlineData("{\"17\":[0]}")]
        [InlineData("{\"1\":[0.5]}")]
        [InlineData("{\"1\":[0,0]}")]
        [InlineData("{\"1\":[4]}")]
        [InlineData("{\"1\":[3]}")]
        [InlineData("{\"1\":[]}")]
        public async Task InvalidOrExcludingMapCannotSave(string json)
        {
            using var fixture = new MidiImportDawFixture();
            string mapPath = Path.Combine(fixture.DirectoryPath, "map.json");
            File.WriteAllText(mapPath, json);
            await fixture.Import.PrepareAsync(new MidiImportInput
            {
                SourcePath = fixture.SourcePath, DestinationPath = fixture.DestinationPath,
                Chip = ChipKind.Nes, ChannelMapPath = mapPath
            });
            Assert.False(fixture.Import.CanSave);
            await fixture.Import.SaveAsync();
            Assert.False(File.Exists(fixture.DestinationPath));
            Assert.Equal(json, File.ReadAllText(mapPath));
        }

        /// <summary>不正 UTF-8 を置換して取り込まず、入力エラーとして表示する。</summary>
        [Fact]
        public async Task InvalidUtf8MapIsRejected()
        {
            using var fixture = new MidiImportDawFixture();
            string mapPath = Path.Combine(fixture.DirectoryPath, "map.json");
            byte[] bytes = { 0xFF, 0xFE };
            File.WriteAllBytes(mapPath, bytes);
            await fixture.Import.PrepareAsync(new MidiImportInput
            {
                SourcePath = fixture.SourcePath, DestinationPath = fixture.DestinationPath,
                Chip = ChipKind.Nes, ChannelMapPath = mapPath
            });
            Assert.Contains("channel-map", fixture.Import.StatusText);
            Assert.False(fixture.Import.CanSave);
            Assert.Equal(bytes, File.ReadAllBytes(mapPath));
        }

        /// <summary>入力欠落・I/O・壊れた SMF は表示して現文書を保つ。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("missing.mid")]
        [InlineData("invalid.mid")]
        public async Task InvalidSourceDoesNotAffectDocument(string sourceName)
        {
            using var fixture = new MidiImportDawFixture();
            File.WriteAllText(Path.Combine(fixture.DirectoryPath, "invalid.mid"), "invalid");
            string sourcePath = sourceName.Length == 0 ? string.Empty : Path.Combine(fixture.DirectoryPath, sourceName);
            await fixture.Import.PrepareAsync(new MidiImportInput
            {
                SourcePath = sourcePath, DestinationPath = fixture.DestinationPath, Chip = ChipKind.Nes
            });
            Assert.False(fixture.Import.CanSave);
            Assert.False(File.Exists(fixture.DestinationPath));
            Assert.Equal(0, fixture.Editor.Document.Session.History.UndoCount);
            Assert.False(fixture.Editor.Presenter.IsDirty);
        }

        /// <summary>入力・map・現文書と同じ保存先を拒否する。</summary>
        [Fact]
        public async Task InputMapAndCurrentDocumentAreProtectedDestinations()
        {
            using var fixture = new MidiImportDawFixture();
            string mapPath = Path.Combine(fixture.DirectoryPath, "map.json");
            File.WriteAllText(mapPath, "{\"1\":[0]}");
            foreach (string path in new[] { fixture.SourcePath, mapPath, fixture.Editor.Path })
            {
                byte[] before = File.ReadAllBytes(path);
                await fixture.Import.PrepareAsync(new MidiImportInput
                {
                    SourcePath = fixture.SourcePath, DestinationPath = path, Chip = ChipKind.Nes, ChannelMapPath = mapPath
                });
                Assert.False(fixture.Import.CanSave);
                Assert.Contains("同じパス", fixture.Import.StatusText);
                Assert.Equal(before, File.ReadAllBytes(path));
            }
        }
    }
}
