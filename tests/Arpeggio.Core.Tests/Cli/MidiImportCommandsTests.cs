using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>SMF から CLI の新規 JSON・診断・空履歴へ至る契約を検証する。</summary>
    [Collection("Cli")]
    public sealed class MidiImportCommandsTests
    {
        /// <summary>三チップと両 SMF 形式で全固定トラック・有効 JSON・入力不変を保つ。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes, 5, 0)]
        [InlineData("nes", ChipKind.Nes, 5, 1)]
        [InlineData("gameboy", ChipKind.GameBoy, 4, 0)]
        [InlineData("gameboy", ChipKind.GameBoy, 4, 1)]
        [InlineData("snes", ChipKind.Snes, 8, 0)]
        [InlineData("snes", ChipKind.Snes, 8, 1)]
        public void ImportsEachChipAndFormat(string chip, ChipKind expectedChip, int tracks, int format)
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture, format: format);
            byte[] before = File.ReadAllBytes(source);
            string output = fixture.PathFor("imported.arpeggio.json");
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", chip, "--json");
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.True(document.RootElement.GetProperty("written").GetBoolean());
            Assert.Equal(new FileInfo(output).Length, document.RootElement.GetProperty("report").GetProperty("outputBytes").GetInt64());
            Song song = SongSerializer.Load(output);
            Assert.Equal(expectedChip, song.Chip);
            Assert.Equal(tracks, song.Tracks.Count);
            Assert.Equal("input", song.Title);
            Assert.Equal(120, song.TempoBpm);
            Assert.Equal(48, song.LengthTicks);
            Assert.Single(song.Tracks.SelectMany(track => track.Notes));
            Assert.All(song.Tracks, track =>
            {
                Assert.False(track.Muted);
                Assert.Equal(0, track.Pan);
                Assert.Null(track.DefaultInstrumentId);
            });
            Assert.Equal(SongSerializer.Serialize(song), File.ReadAllText(output));
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal(1, saved.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(before, File.ReadAllBytes(source));
            AssertEmptyHistory(output);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp", SearchOption.AllDirectories));
        }

        /// <summary>基準 tempo の明示・省略と量子化を、120→60 BPM の固定例で検証する。</summary>
        [Theory]
        [InlineData(false, 120, 48, 96, 144)]
        [InlineData(true, 60, 24, 48, 72)]
        public void BakesTempoMapAtSelectedBaseTempo(bool explicitTempo, int tempo, int secondStart, int secondDuration, int length)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.PathFor("input.mid");
            File.WriteAllBytes(source, MidiFileFixture.Create(
                MidiFileFixture.Bytes("00 FF 51 03 07 A1 20 83 60 FF 51 03 0F 42 40 83 60 FF 2F 00"),
                MidiFileFixture.Bytes("00 90 3C 7F 83 60 80 3C 00 00 90 3E 7F 83 60 80 3E 00 00 FF 2F 00")));
            string output = fixture.PathFor("output.json");
            string[] arguments = { "import", "midi", source, output, "--chip", "nes", "--quantize-ticks", "12", "--json" };
            if (explicitTempo)
            {
                arguments = arguments.Concat(new[] { "--tempo", "60" }).ToArray();
            }
            var result = CliConversionFixture.Invoke(arguments);
            Assert.Equal(0, result.ExitCode);
            Song song = SongSerializer.Load(output);
            Assert.Equal(tempo, song.TempoBpm);
            Assert.Equal(length, song.LengthTicks);
            Assert.Equal(secondStart, song.Tracks[0].Notes[1].Tick);
            Assert.Equal(secondDuration, song.Tracks[0].Notes[1].DurationTicks);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Contains(document.RootElement.GetProperty("report").GetProperty("warnings").EnumerateArray(),
                warning => warning.GetProperty("code").GetString() == "TempoMapFlattened");
        }

        /// <summary>明示タイトルを優先し、省略時は MIDI のトラック名を使う。</summary>
        [Theory]
        [InlineData(false, "Name")]
        [InlineData(true, "指定曲名")]
        public void AppliesTitlePriority(bool explicitTitle, string expected)
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture, "00 FF 03 04 4E 61 6D 65 00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00");
            string output = fixture.PathFor("output.json");
            string[] arguments = { "import", "midi", source, output, "--chip", "nes" };
            if (explicitTitle)
            {
                arguments = arguments.Concat(new[] { "--title", expected }).ToArray();
            }
            Assert.Equal(0, CliConversionFixture.Invoke(arguments).ExitCode);
            Assert.Equal(expected, SongSerializer.Load(output).Title);
        }

        /// <summary>dry-run と strict は既存 JSON・側車・入力を保ち、全診断と予定サイズを返す。</summary>
        [Theory]
        [InlineData(false, false, 0)]
        [InlineData(true, false, 0)]
        [InlineData(false, true, 1)]
        [InlineData(true, true, 1)]
        public void PreviewAndStrictNeverChangeFiles(bool destinationExists, bool strict, int expectedExit)
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            string output = fixture.PathFor("output.json");
            if (destinationExists)
            {
                File.WriteAllText(output, "existing JSON");
            }
            Directory.CreateDirectory(output + ".history");
            string history = output + ".history/state.json";
            File.WriteAllText(history, "existing history");
            string[] arguments = { "import", "midi", source, output, "--chip", "nes", "--dry-run", "--json" };
            if (strict)
            {
                arguments = arguments.Concat(new[] { "--strict" }).ToArray();
            }
            var result = CliConversionFixture.Invoke(arguments);
            Assert.Equal(expectedExit, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(destinationExists, document.RootElement.GetProperty("destinationExists").GetBoolean());
            Assert.False(document.RootElement.GetProperty("written").GetBoolean());
            Assert.True(document.RootElement.GetProperty("report").GetProperty("outputBytes").GetInt64() > 0);
            Assert.Contains(document.RootElement.GetProperty("report").GetProperty("warnings").EnumerateArray(),
                warning => warning.GetProperty("code").GetString() == "ProgramApproximated");
            Assert.Equal(destinationExists, File.Exists(output));
            if (destinationExists)
            {
                Assert.Equal("existing JSON", File.ReadAllText(output));
            }
            Assert.Equal("existing history", File.ReadAllText(history));
            Assert.Equal(1, CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--strict", "--json").ExitCode);
            Assert.Equal("existing history", File.ReadAllText(history));
            Assert.Equal(destinationExists, File.Exists(output));
        }

        /// <summary>通常の警告は成功を維持し、制限は stdout、位置付き警告は stderr に出す。</summary>
        [Fact]
        public void PrintsWarningsAndLimitationsWithoutFailing()
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            var result = CliConversionFixture.Invoke("import", "midi", source, fixture.PathFor("output.json"), "--chip", "nes", "--dry-run");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("制限:", result.Output);
            Assert.Contains("ProgramApproximated", result.Error);
            Assert.Contains("channel=1", result.Error);
            Assert.False(Directory.Exists(fixture.PathFor("output.json.history")));
        }

        /// <summary>既存出力は置換せず、入力同一パスも dry-run を含め拒否する。</summary>
        [Fact]
        public void ProtectsExistingJsonAndInput()
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            byte[] before = File.ReadAllBytes(source);
            string output = fixture.PathFor("output.json");
            File.WriteAllText(output, "keep");
            Directory.CreateDirectory(output + ".history");
            File.WriteAllText(output + ".history/state.json", "history");
            Assert.Equal(3, CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--json").ExitCode);
            Assert.Equal("keep", File.ReadAllText(output));
            Assert.Equal("history", File.ReadAllText(output + ".history/state.json"));
            Assert.Equal(1, CliConversionFixture.Invoke("import", "midi", source, source, "--chip", "nes", "--json").ExitCode);
            Assert.Equal(1, CliConversionFixture.Invoke("import", "midi", source, source, "--chip", "nes", "--dry-run", "--json").ExitCode);
            Assert.Equal(before, File.ReadAllBytes(source));
        }

        /// <summary>壊れた MIDI は 1、入力・出力 I/O は 3 とし、失敗時も report を返す。</summary>
        [Theory]
        [InlineData("broken", 1)]
        [InlineData("empty", 1)]
        [InlineData("missing", 3)]
        [InlineData("destination", 3)]
        public void ReportsConversionAndInputOutputFailures(string scenario, int expectedExit)
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            string output = fixture.PathFor("output.json");
            switch (scenario)
            {
                case "broken": File.WriteAllText(source, "broken SMF"); break;
                case "empty": File.WriteAllBytes(source, MidiFileFixture.Create(0, 480, MidiFileFixture.Bytes("00 FF 2F 00"))); break;
                case "missing": File.Delete(source); break;
                case "destination": output = fixture.PathFor("missing/output.json"); break;
            }
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--json");
            Assert.Equal(expectedExit, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(expectedExit, document.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("report").ValueKind);
            Assert.False(File.Exists(output));
            Assert.False(Directory.Exists(output + ".history"));
        }

        /// <summary>設定値の範囲・polyphony と上書きオプションを拒否する。</summary>
        [Theory]
        [InlineData("--tempo", "0")]
        [InlineData("--tempo", "1001")]
        [InlineData("--quantize-ticks", "5")]
        [InlineData("--quantize-ticks", "0")]
        [InlineData("--polyphony", "unknown")]
        [InlineData("--overwrite", "true")]
        public void RejectsInvalidOptions(string option, string value)
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            string output = fixture.PathFor("output.json");
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", option, value, "--json");
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.NotEmpty(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray());
            Assert.False(File.Exists(output));
        }

        /// <summary>chip は必須で、未知のチップ名も操作エラー。</summary>
        [Fact]
        public void RequiresRecognizedChip()
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            string output = fixture.PathFor("output.json");
            var missing = CliConversionFixture.Invoke("import", "midi", source, output, "--json");
            Assert.Equal(1, missing.ExitCode);
            using JsonDocument missingDocument = JsonDocument.Parse(missing.Output);
            Assert.Equal("InvalidArguments", missingDocument.RootElement.GetProperty("code").GetString());
            var invalid = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "invalid", "--json");
            Assert.Equal(1, invalid.ExitCode);
            using JsonDocument invalidDocument = JsonDocument.Parse(invalid.Output);
            Assert.Equal("InvalidOptions", invalidDocument.RootElement.GetProperty("code").GetString());
        }

        /// <summary>同じ current を持つ古い側車でも、新規取り込みは空履歴から始める。</summary>
        [Fact]
        public void ReplacesMatchingStaleHistoryWithEmptyHistory()
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            string output = fixture.PathFor("output.json");
            using var input = new MemoryStream(File.ReadAllBytes(source));
            MidiImportResult expected = MidiImporter.Import(input, new MidiImportOptions { Chip = ChipKind.Nes, SourceName = source });
            Assert.NotNull(expected.Json);
            string oldSong = SongSerializer.Serialize(SongFactory.Create(ChipKind.Nes));
            Directory.CreateDirectory(output + ".history");
            File.WriteAllText(output + ".history/state.json", SessionOutput.Serialize(new
            {
                current = expected.Json, undo = new[] { oldSong }, redo = new[] { oldSong }
            }));
            Assert.Equal(0, CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--json").ExitCode);
            Assert.Equal(expected.Json, File.ReadAllText(output));
            AssertEmptyHistory(output);
            Assert.Equal(1, CliConversionFixture.Invoke("undo", output).ExitCode);
            Assert.Equal(1, CliConversionFixture.Invoke("redo", output).ExitCode);
            Assert.Equal(0, CliConversionFixture.Invoke("note", "add", output, "--track", "1", "--tick", "0", "--duration", "24", "--note", "64").ExitCode);
            Assert.Equal(0, CliConversionFixture.Invoke("undo", output).ExitCode);
            Assert.Equal(expected.Json, File.ReadAllText(output));
        }

        /// <summary>履歴保存に失敗した新規 JSON を取り消し、既存側車を保つ。</summary>
        [Fact]
        public void HistoryFailureRollsBackNewJson()
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture);
            string output = fixture.PathFor("output.json");
            File.WriteAllText(output + ".history", "履歴ディレクトリと衝突する既存ファイル");
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--json");
            Assert.Equal(3, result.ExitCode);
            Assert.False(File.Exists(output));
            Assert.Equal("履歴ディレクトリと衝突する既存ファイル", File.ReadAllText(output + ".history"));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>CLI の量子化指定が実際のノート開始・終了を変更する。</summary>
        [Fact]
        public void QuantizeOptionChangesNoteBoundaries()
        {
            using var fixture = new CliConversionFixture();
            string source = WriteMidi(fixture, "64 90 3C 7F 64 80 3C 00 00 FF 2F 00");
            string output = fixture.PathFor("output.json");
            Assert.Equal(0, CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--quantize-ticks", "12", "--json").ExitCode);
            Note note = Assert.Single(SongSerializer.Load(output).Tracks[0].Notes);
            Assert.Equal(12, note.Tick);
            Assert.Equal(12, note.DurationTicks);
        }

        internal static string WriteMidi(CliConversionFixture fixture,
            string events = "00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00", int format = 0)
        {
            string source = fixture.PathFor("input.mid");
            File.WriteAllBytes(source, MidiFileFixture.Create(format, 480, MidiFileFixture.Bytes(events)));
            return source;
        }

        private static void AssertEmptyHistory(string output)
        {
            using JsonDocument history = JsonDocument.Parse(File.ReadAllText(output + ".history/state.json"));
            Assert.Equal(File.ReadAllText(output), history.RootElement.GetProperty("current").GetString());
            Assert.Empty(history.RootElement.GetProperty("undo").EnumerateArray());
            Assert.Empty(history.RootElement.GetProperty("redo").EnumerateArray());
            var information = CliConversionFixture.Invoke("info", output, "--json");
            Assert.Equal(0, information.ExitCode);
            using JsonDocument document = JsonDocument.Parse(information.Output);
            Assert.Equal(0, document.RootElement.GetProperty("undoCount").GetInt32());
            Assert.Equal(0, document.RootElement.GetProperty("redoCount").GetInt32());
        }
    }
}
