using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Cli;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp
{
    /// <summary>三変換ツールの JSON 契約・現在曲・安全な保存とセッション不変性を検証する。</summary>
    public sealed class ConversionToolsTests
    {
        /// <summary>公開引数の名前と既定値が設計どおりで、不要な音声設定・上書き引数を持たない。</summary>
        [Fact]
        public void ExposesExactConversionParameters()
        {
            AssertParameters(nameof(ArpeggioTools.ExportNsf),
                new[] { "path", "loops", "author", "copyright", "strict", "dryRun", "overwrite" },
                new object?[] { 1, "", "", false, false, false });
            AssertParameters(nameof(ArpeggioTools.ExportVgm),
                new[] { "path", "loops", "author", "strict", "dryRun", "overwrite" },
                new object?[] { 1, "", false, false, false });
            AssertParameters(nameof(ArpeggioTools.ImportMidi),
                new[] { "midiPath", "path", "chip", "tempo", "quantizeTicks", "polyphony", "channelMap", "title", "strict", "dryRun" },
                new object?[] { null, 1, "steal-oldest", null, null, false, false });
        }

        /// <summary>ディスクの旧曲ではなく現在曲を変換し、事前診断と実保存が共通サービスに一致する。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf, ChipKind.Nes)]
        [InlineData(ConversionFormat.Vgm, ChipKind.Nes)]
        [InlineData(ConversionFormat.Vgm, ChipKind.GameBoy)]
        public void ExportsCurrentSongWithCompleteReport(ConversionFormat format, ChipKind chip)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong(chip);
            var session = new EditSession();
            session.Open(source);
            var tools = new ArpeggioTools(session);
            session.Song!.Title = "Current unsaved title";
            byte[] originalFile = File.ReadAllBytes(source);
            string originalSong = SongSerializer.Serialize(session.Song);
            string output = fixture.PathFor("output");
            ChipExportPlan expected = ChipExportService.Prepare(session.Song, new ChipExportOptions
            {
                Format = format, Loops = 2, Author = "author", Copyright = format == ConversionFormat.Nsf ? "rights" : ""
            });
            JsonElement preview = Reply(Export(tools, format, output, dryRun: true), 0);
            Assert.False(preview.GetProperty("written").GetBoolean());
            Assert.True(preview.GetProperty("dryRun").GetBoolean());
            Assert.False(preview.GetProperty("destinationExists").GetBoolean());
            Assert.Equal(SessionOutput.Serialize(expected.Report), SessionOutput.Serialize(preview.GetProperty("report")));
            Assert.False(File.Exists(output));
            JsonElement saved = Reply(Export(tools, format, output), 0);
            Assert.True(saved.GetProperty("written").GetBoolean());
            using var stream = new MemoryStream();
            Assert.True(ChipExportService.Write(expected, stream));
            Assert.Equal(stream.ToArray(), File.ReadAllBytes(output));
            Assert.Equal(originalFile, File.ReadAllBytes(source));
            Assert.Equal(originalSong, SongSerializer.Serialize(session.Song));
            Assert.Equal(0, session.History.UndoCount);
        }

        /// <summary>既存出力への dry-run は成功し、既定保存・入力同一パスを拒否して明示上書きだけを許す。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Vgm)]
        public void ProtectsExistingOutputAndSource(ConversionFormat format)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            var session = new EditSession();
            session.Open(source);
            var tools = new ArpeggioTools(session);
            string output = fixture.PathFor("existing");
            File.WriteAllText(output, "keep");
            JsonElement preview = Reply(Export(tools, format, output, dryRun: true), 0);
            Assert.True(preview.GetProperty("destinationExists").GetBoolean());
            JsonElement rejected = Reply(Export(tools, format, output), 3);
            Assert.Equal("keep", File.ReadAllText(output));
            Assert.Equal(preview.GetProperty("report").GetRawText(), rejected.GetProperty("report").GetRawText());
            Reply(Export(tools, format, output, overwrite: true), 0);
            Assert.NotEqual("keep", File.ReadAllText(output));
            byte[] sourceBytes = File.ReadAllBytes(source);
            Reply(Export(tools, format, source, dryRun: true, overwrite: true), 1);
            Reply(Export(tools, format, source, overwrite: true), 1);
            Assert.Equal(sourceBytes, File.ReadAllBytes(source));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>操作・文書・I/O の失敗はすべて report 付き JSON になり、strict でも全サイズを算定する。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Vgm)]
        public void ClassifiesFailuresAndStrictWarnings(ConversionFormat format)
        {
            using var fixture = new CliConversionFixture();
            var session = new EditSession();
            var tools = new ArpeggioTools(session);
            string output = fixture.PathFor("output");
            Reply(Export(tools, format, output), 1);
            session.Open(fixture.CreateSong());
            session.Song!.Tracks[0].Pan = 0.25;
            JsonElement strict = Reply(Export(tools, format, output, strict: true), 1);
            Assert.True(strict.GetProperty("report").GetProperty("warningCount").GetInt64() > 0);
            Assert.True(strict.GetProperty("report").GetProperty("outputBytes").GetInt64() > 0);
            Assert.False(File.Exists(output));
            Reply(Export(tools, format, fixture.PathFor("missing/output")), 3);
            session.Song.TempoBpm = 0;
            Reply(Export(tools, format, output), 2);
        }

        /// <summary>対象外チップと loops は共通レポートの操作エラーとなり、出力を作らない。</summary>
        [Theory]
        [InlineData("gameboy", true)]
        [InlineData("snes", true)]
        [InlineData("snes", false)]
        [InlineData("nes", false)]
        public void RejectsUnsupportedExportOptions(string chip, bool isNsf)
        {
            using var fixture = new CliConversionFixture();
            var tools = new ArpeggioTools(new EditSession());
            tools.NewSong(fixture.PathFor("song.json"), chip);
            string output = fixture.PathFor("output");
            string reply = isNsf ? tools.ExportNsf(output) : tools.ExportVgm(output, loops: chip == "nes" ? 0 : 1);
            JsonElement result = Reply(reply, 1);
            Assert.True(result.GetProperty("report").GetProperty("errorCount").GetInt64() > 0);
            Assert.False(File.Exists(output));
        }

        /// <summary>未オープンでも全チップへ取り込め、JSON version と入力ファイル・セッションを保つ。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes)]
        [InlineData("gameboy", ChipKind.GameBoy)]
        [InlineData("snes", ChipKind.Snes)]
        public void ImportsWithoutOpeningSong(string chip, ChipKind expectedChip)
        {
            using var fixture = new CliConversionFixture();
            var session = new EditSession();
            var tools = new ArpeggioTools(session);
            string source = CreateMidi(fixture);
            byte[] original = File.ReadAllBytes(source);
            string output = fixture.PathFor("imported.arpeggio.json");
            JsonElement preview = Reply(tools.ImportMidi(source, output, chip, tempo: 100, title: "Imported", dryRun: true), 0);
            Assert.False(File.Exists(output));
            JsonElement saved = Reply(tools.ImportMidi(source, output, chip, tempo: 100, title: "Imported"), 0);
            Assert.Equal(preview.GetProperty("report").GetRawText(), saved.GetProperty("report").GetRawText());
            Song song = SongSerializer.Load(output);
            Assert.Equal(expectedChip, song.Chip);
            Assert.Equal(100, song.TempoBpm);
            Assert.Equal("Imported", song.Title);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(original, File.ReadAllBytes(source));
            Assert.Null(session.Song);
            Assert.Null(session.Path);
            Assert.Equal(0, session.History.UndoCount);
        }

        /// <summary>import の成功・dry-run・strict・失敗・競合は開いている曲と undo / redo の両履歴を維持する。</summary>
        [Fact]
        public void ImportKeepsSharedSongAndHistoryForEveryOutcome()
        {
            using var fixture = new CliConversionFixture();
            var session = new EditSession();
            var tools = new ArpeggioTools(session);
            string current = fixture.PathFor("current.json");
            tools.NewSong(current, "nes");
            tools.AddNote(0, 0, 24, "C4");
            tools.AddNote(0, 24, 24, "E4");
            tools.Undo();
            Song original = session.Song!;
            string before = tools.SongInfo();
            byte[] bytes = File.ReadAllBytes(current);
            string source = CreateMidi(fixture);
            string output = fixture.PathFor("imported.json");
            Reply(tools.ImportMidi(source, output, "nes", dryRun: true), 0);
            Reply(tools.ImportMidi(source, output, "nes", strict: true), 1);
            Assert.False(File.Exists(output));
            Reply(tools.ImportMidi(source, output, "nes", channelMap: "{\"1\":[1]}"), 0);
            Assert.Single(SongSerializer.Load(output).Tracks[1].Notes);
            byte[] saved = File.ReadAllBytes(output);
            Reply(tools.ImportMidi(source, output, "nes"), 3);
            Assert.True(Reply(tools.ImportMidi(source, output, "nes", dryRun: true), 0).GetProperty("destinationExists").GetBoolean());
            Reply(tools.ImportMidi(source, current, "nes"), 3);
            Reply(tools.ImportMidi(source, source, "nes", dryRun: true), 1);
            Reply(tools.ImportMidi(fixture.PathFor("missing.mid"), output, "nes"), 3);
            Assert.Same(original, session.Song);
            Assert.Equal(before, tools.SongInfo());
            Assert.Equal(bytes, File.ReadAllBytes(current));
            Assert.Equal(saved, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>JSON 文字列 map の型・重複・チップ適合、設定不正、壊れた MIDI を拒否する。</summary>
        [Theory]
        [InlineData("{")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{\"1\":[0],\"01\":[1]}")]
        [InlineData("{\"17\":[0]}")]
        [InlineData("{\"1\":[0.5]}")]
        [InlineData("{\"1\":[4]}")]
        public void RejectsInvalidMidiInputAndMap(string channelMap)
        {
            using var fixture = new CliConversionFixture();
            var tools = new ArpeggioTools(new EditSession());
            string source = CreateMidi(fixture);
            string output = fixture.PathFor("output.json");
            Reply(tools.ImportMidi(source, output, "nes", channelMap: channelMap), 1);
            Reply(tools.ImportMidi(source, output, "unknown"), 1);
            Reply(tools.ImportMidi(source, output, "nes", quantizeTicks: 5), 1);
            Reply(tools.ImportMidi(source, output, "nes", tempo: 0), 1);
            Reply(tools.ImportMidi(source, output, "nes", polyphony: "unknown"), 1);
            File.WriteAllText(source, "broken");
            Reply(tools.ImportMidi(source, output, "nes"), 1);
            Assert.False(File.Exists(output));
        }

        private static string Export(ArpeggioTools tools, ConversionFormat format, string path,
            bool dryRun = false, bool overwrite = false, bool strict = false)
        {
            return format == ConversionFormat.Nsf
                ? tools.ExportNsf(path, loops: 2, author: "author", copyright: "rights", strict: strict, dryRun: dryRun, overwrite: overwrite)
                : tools.ExportVgm(path, loops: 2, author: "author", strict: strict, dryRun: dryRun, overwrite: overwrite);
        }

        private static JsonElement Reply(string json, int exitCode)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement result = document.RootElement;
            Assert.Equal(exitCode, result.GetProperty("exitCode").GetInt32());
            Assert.Equal(JsonValueKind.Object, result.GetProperty("report").ValueKind);
            if (exitCode == 0)
            {
                Assert.False(result.TryGetProperty("error", out _));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("error").GetString()));
                Assert.False(result.GetProperty("written").GetBoolean());
            }
            return result.Clone();
        }

        private static string CreateMidi(CliConversionFixture fixture)
        {
            string source = fixture.PathFor("source.mid");
            File.WriteAllBytes(source, MidiFileFixture.Create(0, 480,
                MidiFileFixture.Bytes("00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00")));
            return source;
        }

        private static void AssertParameters(string name, string[] names, object?[] defaults)
        {
            ParameterInfo[] parameters = typeof(ArpeggioTools).GetMethod(name)!.GetParameters();
            Assert.Equal(names, parameters.Select(parameter => parameter.Name));
            Assert.Equal(defaults, parameters.Where(parameter => parameter.HasDefaultValue).Select(parameter => parameter.DefaultValue));
        }
    }
}
