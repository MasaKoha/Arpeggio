using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Tests.Formats;
using Xunit;
using Arpeggio.Core.Tests.Formats.Export.Nsf;
using Arpeggio.Core.Tests.Formats.Export.Vgm;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>NSF／VGM の CLI 引数から実保存・独立パース・終了コードまでを検証する。</summary>
    [Collection("Cli")]
    public sealed class ChipExportCommandsTests
    {
        /// <summary>対応する全形式・チップで事前診断と保存のサイズが一致し、元 JSON と履歴は不変。</summary>
        [Theory]
        [InlineData("nsf", ChipKind.Nes)]
        [InlineData("vgm", ChipKind.Nes)]
        [InlineData("vgm", ChipKind.GameBoy)]
        public void PreparesAndWritesSupportedFormats(string format, ChipKind chip)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong(chip);
            string output = fixture.PathFor("output." + format);
            byte[] before = File.ReadAllBytes(source);
            Directory.CreateDirectory(source + ".history");
            string historyPath = source + ".history/state.json";
            File.WriteAllText(historyPath, "履歴を読む必要のない書き出し");
            var preview = CliConversionFixture.Invoke("export", format, source, output, "--loops", "2", "--author", "Composer", "--dry-run", "--json");
            Assert.Equal(0, preview.ExitCode);
            Assert.Empty(preview.Error);
            Assert.False(File.Exists(output));
            using JsonDocument prepared = JsonDocument.Parse(preview.Output);
            Assert.False(prepared.RootElement.GetProperty("written").GetBoolean());
            Assert.False(prepared.RootElement.GetProperty("destinationExists").GetBoolean());
            Assert.NotEmpty(prepared.RootElement.GetProperty("report").GetProperty("limitations").EnumerateArray());
            var saved = CliConversionFixture.Invoke("export", format, source, output, "--loops", "2", "--author", "Composer", "--json");
            Assert.Equal(0, saved.ExitCode);
            Assert.Empty(saved.Error);
            using JsonDocument written = JsonDocument.Parse(saved.Output);
            Assert.True(written.RootElement.GetProperty("written").GetBoolean());
            Assert.Equal(prepared.RootElement.GetProperty("report").GetRawText(), written.RootElement.GetProperty("report").GetRawText());
            Assert.Equal(new FileInfo(output).Length, written.RootElement.GetProperty("report").GetProperty("outputBytes").GetInt64());
            if (format == "nsf")
            {
                IndependentNsfLoader file = IndependentNsfLoader.Load(output);
                Assert.Equal("Test song", file.Title);
                Assert.Equal("Composer", file.Author);
            }
            else
            {
                ParsedVgm file = IndependentVgmParser.Parse(File.ReadAllBytes(output));
                Assert.Equal(88200, file.WaitSamples);
                Assert.Equal("Composer", file.Gd3Fields[7]);
            }
            Assert.Equal(before, File.ReadAllBytes(source));
            Assert.Equal("履歴を読む必要のない書き出し", File.ReadAllText(historyPath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>既存出力は dry-run で診断でき、明示上書きだけが内容を置換する。</summary>
        [Theory]
        [InlineData("nsf")]
        [InlineData("vgm")]
        public void ProtectsExistingDestinationAndSource(string format)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            string output = fixture.PathFor("output." + format);
            File.WriteAllText(output, "keep");
            var preview = CliConversionFixture.Invoke("export", format, source, output, "--dry-run", "--json");
            Assert.Equal(0, preview.ExitCode);
            using JsonDocument document = JsonDocument.Parse(preview.Output);
            Assert.True(document.RootElement.GetProperty("destinationExists").GetBoolean());
            Assert.Equal("keep", File.ReadAllText(output));
            Assert.Equal(3, CliConversionFixture.Invoke("export", format, source, output, "--json").ExitCode);
            Assert.Equal("keep", File.ReadAllText(output));
            Assert.Equal(0, CliConversionFixture.Invoke("export", format, source, output, "--overwrite").ExitCode);
            byte[] before = File.ReadAllBytes(source);
            string alias = Path.Combine(fixture.DirectoryPath, ".", Path.GetFileName(source));
            Assert.Equal(1, CliConversionFixture.Invoke("export", format, source, alias, "--overwrite", "--json").ExitCode);
            Assert.Equal(1, CliConversionFixture.Invoke("export", format, source, alias, "--dry-run", "--json").ExitCode);
            Assert.Equal(before, File.ReadAllBytes(source));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>strict は警告と予定サイズを残して保存を拒否し、通常表示は警告と制限を分ける。</summary>
        [Theory]
        [InlineData("nsf")]
        [InlineData("vgm")]
        public void StrictRejectsWarningsAfterFullPreparation(string format)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            Song song = SongSerializer.Load(source);
            song.Title = "日本語の曲";
            song.Tracks[0].Pan = 0.25;
            SongSerializer.Save(song, source);
            string output = fixture.PathFor("output." + format);
            File.WriteAllText(output, "keep");
            var strict = CliConversionFixture.Invoke("export", format, source, output, "--strict", "--overwrite", "--json");
            Assert.Equal(1, strict.ExitCode);
            Assert.Empty(strict.Error);
            using JsonDocument document = JsonDocument.Parse(strict.Output);
            JsonElement report = document.RootElement.GetProperty("report");
            Assert.True(report.GetProperty("strict").GetBoolean());
            Assert.True(report.GetProperty("outputBytes").GetInt64() > 0);
            Assert.Contains(report.GetProperty("warnings").EnumerateArray(), warning => warning.GetProperty("code").GetString() == "PanReduced");
            if (format == "nsf")
            {
                Assert.Contains(report.GetProperty("warnings").EnumerateArray(), warning => warning.GetProperty("code").GetString() == "MetadataReduced");
                Assert.True(report.GetProperty("statistics").GetProperty("maximumPlayCycles").GetInt64() > 0);
            }
            Assert.Equal("keep", File.ReadAllText(output));
            var normal = CliConversionFixture.Invoke("export", format, source, output, "--dry-run");
            Assert.Equal(0, normal.ExitCode);
            Assert.Contains("制限:", normal.Output);
            Assert.Contains("PanReduced", normal.Error);
            Assert.Contains("track=0", normal.Error);
        }

        /// <summary>チップ不一致は変換診断付きの操作エラー。</summary>
        [Theory]
        [InlineData("nsf", ChipKind.GameBoy)]
        [InlineData("nsf", ChipKind.Snes)]
        [InlineData("vgm", ChipKind.Snes)]
        public void RejectsUnsupportedChips(string format, ChipKind chip)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong(chip);
            string output = fixture.PathFor("output");
            var result = CliConversionFixture.Invoke("export", format, source, output, "--json");
            Assert.Equal(1, result.ExitCode);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Contains(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray(),
                error => error.GetProperty("code").GetString() == "UnsupportedChip");
            Assert.False(File.Exists(output));
        }

        /// <summary>専用外オプション・不正 loops も JSON 一件の操作エラーになる。</summary>
        [Theory]
        [InlineData("nsf", "--sample-rate", "44100")]
        [InlineData("vgm", "--tail", "0")]
        [InlineData("vgm", "--copyright", "owner")]
        [InlineData("nsf", "--loops", "0")]
        [InlineData("vgm", "--loops", "17")]
        [InlineData("nsf", "--loops", "invalid")]
        public void RejectsInvalidArguments(string format, string option, string value)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            var result = CliConversionFixture.Invoke("export", format, source, fixture.PathFor("output"), option, value, "--json");
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.NotEmpty(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray());
        }

        /// <summary>文書不正は 2、読み書き I/O は 3 とし、どの場合も JSON に report を含める。</summary>
        [Theory]
        [InlineData("nsf")]
        [InlineData("vgm")]
        public void MapsDocumentAndInputOutputFailures(string format)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            string output = fixture.PathFor("missing/output");
            var saveFailure = CliConversionFixture.Invoke("export", format, source, output, "--json");
            Assert.Equal(3, saveFailure.ExitCode);
            using JsonDocument failure = JsonDocument.Parse(saveFailure.Output);
            Assert.True(failure.RootElement.GetProperty("report").GetProperty("outputBytes").GetInt64() > 0);
            Assert.Equal(0, failure.RootElement.GetProperty("report").GetProperty("errorCount").GetInt64());
            File.WriteAllText(source, "{}");
            var invalid = CliConversionFixture.Invoke("export", format, source, output, "--json");
            Assert.Equal(2, invalid.ExitCode);
            using JsonDocument invalidDocument = JsonDocument.Parse(invalid.Output);
            Assert.Equal("InvalidSong", invalidDocument.RootElement.GetProperty("code").GetString());
            File.Delete(source);
            var missing = CliConversionFixture.Invoke("export", format, source, output, "--json");
            Assert.Equal(3, missing.ExitCode);
            using JsonDocument missingDocument = JsonDocument.Parse(missing.Output);
            Assert.Equal("InputOutputError", missingDocument.RootElement.GetProperty("code").GetString());
        }

        /// <summary>NSF 固有の権利表記と loops 上限を受け付ける。</summary>
        [Fact]
        public void AcceptsNsfCopyrightAndMaximumLoops()
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            string output = fixture.PathFor("output.nsf");
            Assert.Equal(0, CliConversionFixture.Invoke("export", "nsf", source, output, "--copyright", "Owner", "--loops", "16").ExitCode);
            Assert.Equal("Owner", IndependentNsfLoader.Load(output).Copyright);
        }

        /// <summary>DPCM は元位置付きで拒否し、ミュートした同じ曲は書き出せる。</summary>
        [Theory]
        [InlineData("nsf")]
        [InlineData("vgm")]
        public void RejectsActiveDpcmWithSourceLocation(string format)
        {
            const int DpcmTrack = 4;
            const int DpcmInstrument = 2;
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            Song song = SongSerializer.Load(source);
            song.Instruments.Add(new NesDpcmInstrument { Id = DpcmInstrument, Name = "DPCM" });
            song.Tracks[DpcmTrack].Notes.Add(new Note { InstrumentId = DpcmInstrument });
            SongSerializer.Save(song, source);
            string output = fixture.PathFor("output." + format);
            File.WriteAllText(output, "keep");
            var result = CliConversionFixture.Invoke("export", format, source, output, "--overwrite", "--json");
            Assert.Equal(1, result.ExitCode);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement diagnostic = Assert.Single(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray(),
                error => error.GetProperty("code").GetString() == "UnsupportedDpcm");
            Assert.Equal(DpcmTrack, diagnostic.GetProperty("sourceTrack").GetInt32());
            Assert.Equal(0, diagnostic.GetProperty("sourceTick").GetInt64());
            Assert.Equal("keep", File.ReadAllText(output));
            song.Tracks[DpcmTrack].Muted = true;
            SongSerializer.Save(song, source);
            Assert.Equal(0, CliConversionFixture.Invoke("export", format, source, output, "--overwrite", "--json").ExitCode);
        }

        /// <summary>NSF の極短音衝突を操作エラーとし、インライン JSON フラグも尊重する。</summary>
        [Fact]
        public void ReportsNsfCollisionAndInlineJsonArgumentErrors()
        {
            const int FastTempo = 1000;
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            Song song = SongSerializer.Load(source);
            song.TempoBpm = FastTempo;
            song.Tracks[0].Notes[0].DurationTicks = 1;
            SongSerializer.Save(song, source);
            string output = fixture.PathFor("output.nsf");
            var collision = CliConversionFixture.Invoke("export", "nsf", source, output, "--json");
            Assert.Equal(1, collision.ExitCode);
            using JsonDocument document = JsonDocument.Parse(collision.Output);
            Assert.Contains(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray(),
                error => error.GetProperty("code").GetString() == "NsfEventCollision");
            Assert.False(File.Exists(output));
            var invalid = CliConversionFixture.Invoke("export", "nsf", source, output, "--tail", "0", "--json=true");
            Assert.Equal(1, invalid.ExitCode);
            Assert.Empty(invalid.Error);
            using JsonDocument invalidDocument = JsonDocument.Parse(invalid.Output);
            Assert.Equal("InvalidArguments", invalidDocument.RootElement.GetProperty("code").GetString());
        }
    }
}
