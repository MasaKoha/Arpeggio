using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Cli;
using Arpeggio.Core.Tests.Mcp.Sfx;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Export
{
    /// <summary>パラメータSFXを既存のチップ書き出しへ通し、診断・strict・対応外の契約を守る。</summary>
    [Collection("Cli")]
    public sealed class SfxExportRegressionTests
    {
        private const int ToneTrackIndex = 0;
        private const int NoteStartTick = 0;

        /// <summary>全8用途の定義付き保存列が通常ソングと同じNSF/VGMになり、診断も変わらない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ConversionFormat.Nsf)]
        [InlineData(ChipKind.Nes, ConversionFormat.Vgm)]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Vgm)]
        public void DefinitionDoesNotChangeExportBytesOrDiagnostics(ChipKind chip, ConversionFormat format)
        {
            foreach (SfxParameterPresetDescription preset in SfxParameterPresetCatalog.GetAll(chip))
            {
                Song song = SfxEditor.CreateCandidate(preset.Parameters, chip, preset.Name, preset.Name).Song;
                string original = SongSerializer.Serialize(song);
                Song restored = SongSerializer.Deserialize(original);
                Song detached = SongSerializer.Deserialize(original);
                detached.Sfx = null;
                var options = new ChipExportOptions { Format = format };
                ChipExportPlan expected = ChipExportService.Prepare(detached, options);
                ChipExportPlan actual = ChipExportService.Prepare(restored, options);
                Assert.True(actual.CanWrite, SessionOutput.Serialize(actual.Report));
                Assert.Empty(actual.Report.Errors);
                Assert.NotEmpty(actual.Report.Limitations);
                Assert.Equal(SessionOutput.Serialize(expected.Report), SessionOutput.Serialize(actual.Report));
                using var expectedOutput = new MemoryStream();
                using var actualOutput = new MemoryStream();
                Assert.True(ChipExportService.Write(expected, expectedOutput));
                Assert.True(ChipExportService.Write(actual, actualOutput));
                Assert.Equal(expectedOutput.ToArray(), actualOutput.ToArray());
                Assert.Equal(actualOutput.Length, actual.Report.OutputBytes);
                Assert.Equal(original, SongSerializer.Serialize(restored));
                AssertStrictPreservesDiagnostics(restored, actual, format);
            }
        }

        /// <summary>位置付きの既存警告がCLI/MCPへ届き、strictは予定サイズを残して保存を拒否する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ConversionFormat.Nsf, "NsfTimingQuantized")]
        [InlineData(ChipKind.Nes, ConversionFormat.Vgm, "PulsePhaseRestarted")]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Vgm, "EnvelopeRetriggered")]
        public void ExistingWarningsReachCliAndMcpWithStrictRefusal(ChipKind chip, ConversionFormat format, string warningCode)
        {
            using var fixture = new SfxToolFixture();
            string sourcePath = fixture.PathFor("laser.json");
            SfxToolFixture.Success(fixture.Tools.CreateSfx(sourcePath, chip.ToString().ToLowerInvariant(), "laser"));
            SfxToolFixture.Success(fixture.Tools.OpenSong(sourcePath));
            Song song = fixture.Session.Song!;
            ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = format, Strict = true });
            Assert.False(plan.CanWrite);
            Assert.Contains(plan.Report.Warnings, warning => warning.Code == warningCode &&
                warning.SourceTrack == ToneTrackIndex && warning.SourceTick == NoteStartTick);
            Assert.True(plan.Report.OutputBytes > 0);
            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            string commandPath = fixture.PathFor("command." + format.ToString().ToLowerInvariant());
            string toolPath = fixture.PathFor("tool." + format.ToString().ToLowerInvariant());
            var command = CliConversionFixture.Invoke("export", format.ToString().ToLowerInvariant(), sourcePath,
                commandPath, "--strict", "--json");
            Assert.Equal(1, command.ExitCode);
            Assert.Empty(command.Error);
            string response = format == ConversionFormat.Nsf
                ? fixture.Tools.ExportNsf(toolPath, strict: true)
                : fixture.Tools.ExportVgm(toolPath, strict: true);
            AssertRefusedReport(plan.Report, command.Output);
            AssertRefusedReport(plan.Report, response);
            Assert.False(File.Exists(commandPath));
            Assert.False(File.Exists(toolPath));
            Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            Assert.Equal(0, fixture.Session.History.UndoCount);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>SNESの全8用途とGBのNSFは、SFX定義があっても既存のUnsupportedChipで拒否する。</summary>
        [Theory]
        [InlineData(ChipKind.Snes, ConversionFormat.Nsf)]
        [InlineData(ChipKind.Snes, ConversionFormat.Vgm)]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Nsf)]
        public void UnsupportedChipsRemainUnsupported(ChipKind chip, ConversionFormat format)
        {
            foreach (SfxParameterPresetDescription preset in SfxParameterPresetCatalog.GetAll(chip))
            {
                Song song = SfxEditor.CreateCandidate(preset.Parameters, chip, preset.Name, preset.Name).Song;
                string before = SongSerializer.Serialize(song);
                ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = format });
                Assert.False(plan.CanWrite);
                Assert.Contains(plan.Report.Errors, error => error.Code == "UnsupportedChip");
                using var output = new MemoryStream();
                Assert.False(ChipExportService.Write(plan, output));
                Assert.Equal(0, output.Length);
                Assert.Equal(before, SongSerializer.Serialize(song));
            }
        }

        private static void AssertStrictPreservesDiagnostics(Song song, ChipExportPlan normal, ConversionFormat format)
        {
            ChipExportPlan strict = ChipExportService.Prepare(song, new ChipExportOptions { Format = format, Strict = true });
            Assert.Equal(normal.Report.WarningCount == 0, strict.CanWrite);
            Assert.Equal(normal.Report.OutputBytes, strict.Report.OutputBytes);
            Assert.Equal(SessionOutput.Serialize(normal.Report.Warnings), SessionOutput.Serialize(strict.Report.Warnings));
            Assert.Equal(normal.Report.Limitations, strict.Report.Limitations);
            using var output = new MemoryStream();
            Assert.Equal(strict.CanWrite, ChipExportService.Write(strict, output));
            Assert.Equal(strict.CanWrite ? normal.Report.OutputBytes : 0, output.Length);
        }

        private static void AssertRefusedReport(ConversionReport expected, string json)
        {
            using JsonDocument response = JsonDocument.Parse(json);
            using JsonDocument expectedReport = JsonDocument.Parse(SessionOutput.Serialize(expected));
            JsonElement root = response.RootElement;
            Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
            Assert.False(root.GetProperty("written").GetBoolean());
            Assert.Equal(JsonSerializer.Serialize(expectedReport.RootElement),
                JsonSerializer.Serialize(root.GetProperty("report")));
        }
    }
}
