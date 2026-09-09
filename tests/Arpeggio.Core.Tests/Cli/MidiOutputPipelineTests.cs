using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>CLI と MCP で同じ MIDI を新規保存・Open し、既存音声と M3 の全対応出力まで接続する。</summary>
    [Collection("Cli")]
    public sealed class MidiOutputPipelineTests
    {
        private const int SampleRate = 44100;
        private const int SongFrames = 66150;
        private const int StereoChannels = 2;

        /// <summary>三チップの MIDI→JSON→WAV／OGG と対応 VGM／NSF が両フロントエンドで一致する。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes)]
        [InlineData("gameboy", ChipKind.GameBoy)]
        [InlineData("snes", ChipKind.Snes)]
        public void CliAndMcpImportThenExportAllSupportedFormats(string chipName, ChipKind chip)
        {
            using var fixture = new MidiPipelineFixture();
            byte[] midi = File.ReadAllBytes(fixture.SourcePath);
            var imported = CliConversionFixture.Invoke("import", "midi", fixture.SourcePath, fixture.SongPath, "--chip", chipName, "--json");
            Assert.Equal(0, imported.ExitCode);
            Assert.Empty(imported.Error);
            AssertSuccess(imported.Output);
            byte[] json = File.ReadAllBytes(fixture.SongPath);
            string mcpSong = fixture.PathFor("mcp.arpeggio.json");
            var tools = new ArpeggioTools();
            AssertSuccess(tools.ImportMidi(fixture.SourcePath, mcpSong, chipName));
            Assert.Equal(json, File.ReadAllBytes(mcpSong));
            AssertSuccess(tools.OpenSong(mcpSong));

            ExportAudio(fixture, tools, "wav");
            ExportAudio(fixture, tools, "ogg");
            if (chip != ChipKind.Snes) { ExportChip(fixture, tools, "vgm"); }
            if (chip == ChipKind.Nes) { ExportChip(fixture, tools, "nsf"); }

            Assert.Equal(json, File.ReadAllBytes(fixture.SongPath));
            Assert.Equal(json, File.ReadAllBytes(mcpSong));
            Assert.Equal(midi, File.ReadAllBytes(fixture.SourcePath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        private static void ExportAudio(MidiPipelineFixture fixture, ArpeggioTools tools, string format)
        {
            string cliPath = fixture.PathFor("cli." + format);
            string mcpPath = fixture.PathFor("mcp." + format);
            var exported = CliConversionFixture.Invoke("export", format, fixture.SongPath, cliPath, "--tail", "0", "--sample-rate", "44100");
            Assert.Equal(0, exported.ExitCode);
            AssertSuccess(format == "wav" ? tools.ExportWav(mcpPath, tail: 0) : tools.ExportOgg(mcpPath, tail: 0));
            if (format == "wav")
            {
                Assert.Equal(File.ReadAllBytes(cliPath), File.ReadAllBytes(mcpPath));
                float[] samples = WavReader.Read(cliPath, out int sampleRate);
                Assert.Equal(SampleRate, sampleRate);
                Assert.Equal(SongFrames * StereoChannels, samples.Length);
                Assert.Contains(samples, sample => Math.Abs(sample) > 0.001f);
                return;
            }
            MidiPipelineTests.AssertOggEndOfStream(File.ReadAllBytes(cliPath), SongFrames);
            MidiPipelineTests.AssertOggEndOfStream(File.ReadAllBytes(mcpPath), SongFrames);
        }

        private static void ExportChip(MidiPipelineFixture fixture, ArpeggioTools tools, string format)
        {
            string cliPath = fixture.PathFor("cli." + format);
            string mcpPath = fixture.PathFor("mcp." + format);
            var exported = CliConversionFixture.Invoke("export", format, fixture.SongPath, cliPath, "--json");
            Assert.Equal(0, exported.ExitCode);
            AssertSuccess(exported.Output);
            AssertSuccess(format == "nsf" ? tools.ExportNsf(mcpPath) : tools.ExportVgm(mcpPath));
            Assert.Equal(File.ReadAllBytes(cliPath), File.ReadAllBytes(mcpPath));
            if (format == "vgm")
            {
                Assert.Equal(SongFrames, IndependentVgmParser.Parse(File.ReadAllBytes(cliPath)).WaitSamples);
            }
            else
            {
                Assert.Equal("input", IndependentNsfLoader.Load(cliPath).Title);
            }
        }

        private static void AssertSuccess(string response)
        {
            using JsonDocument document = JsonDocument.Parse(response);
            Assert.False(document.RootElement.TryGetProperty("error", out _), response);
            if (document.RootElement.TryGetProperty("exitCode", out JsonElement exitCode)) { Assert.Equal(0, exitCode.GetInt32()); }
            if (document.RootElement.TryGetProperty("written", out JsonElement written)) { Assert.True(written.GetBoolean()); }
        }
    }
}
