using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>map ファイルの厳密な読み取りと既存声割り当てへの接続を検証する。</summary>
    [Collection("Cli")]
    public sealed class MidiChannelMapCommandsTests
    {
        /// <summary>指定チャンネルの候補上書きと空配列の除外を三チップへ適用する。</summary>
        [Theory]
        [InlineData("nes")]
        [InlineData("gameboy")]
        [InlineData("snes")]
        public void AppliesMapAndExplicitExclusion(string chip)
        {
            using var fixture = new CliConversionFixture();
            string source = MidiImportCommandsTests.WriteMidi(fixture,
                "00 90 3C 7F 00 91 40 7F 83 60 80 3C 00 00 81 40 00 00 FF 2F 00");
            string map = fixture.PathFor("map.json");
            const string Mapping = "{\"1\":[1],\"2\":[]}";
            File.WriteAllText(map, Mapping);
            string output = fixture.PathFor("output.json");
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", chip, "--channel-map", map, "--json");
            Assert.Equal(0, result.ExitCode);
            Song song = SongSerializer.Load(output);
            Assert.Single(song.Tracks[1].Notes);
            Assert.Single(song.Tracks.SelectMany(track => track.Notes));
            Assert.Equal(Mapping, File.ReadAllText(map));
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(1, document.RootElement.GetProperty("report").GetProperty("statistics").GetProperty("explicitlyExcludedMidiChannels").GetInt64());
        }

        /// <summary>一声候補の競合で steal-oldest / drop-new の違いが出力ノートへ反映される。</summary>
        [Theory]
        [InlineData("steal-oldest", 2, 24, "NoteTruncated")]
        [InlineData("drop-new", 1, 48, "PolyphonyReduced")]
        public void AppliesPolyphonyToMappedVoice(string mode, int count, int firstDuration, string code)
        {
            using var fixture = new CliConversionFixture();
            string source = MidiImportCommandsTests.WriteMidi(fixture,
                "00 90 3C 7F 81 70 90 40 7F 81 70 80 3C 00 81 70 80 40 00 00 FF 2F 00");
            string map = fixture.PathFor("map.json");
            File.WriteAllText(map, "{\"1\":[0]}");
            string output = fixture.PathFor("output.json");
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--channel-map", map, "--polyphony", mode, "--json");
            Assert.Equal(0, result.ExitCode);
            Song song = SongSerializer.Load(output);
            Assert.Equal(count, song.Tracks[0].Notes.Count);
            Assert.Equal(firstDuration, song.Tracks[0].Notes[0].DurationTicks);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Contains(document.RootElement.GetProperty("report").GetProperty("warnings").EnumerateArray(),
                warning => warning.GetProperty("code").GetString() == code);
        }

        /// <summary>JSON 構造・数値キー・候補型・範囲・チップ互換性を拒否する。</summary>
        [Theory]
        [InlineData("[]")]
        [InlineData("{")]
        [InlineData("null")]
        [InlineData("{\"1\":null}")]
        [InlineData("{\"1\":1}")]
        [InlineData("{\"1\":[0],\"1\":[1]}")]
        [InlineData("{\"1\":[0],\"01\":[1]}")]
        [InlineData("{\"0\":[0]}")]
        [InlineData("{\"17\":[0]}")]
        [InlineData("{\"name\":[0]}")]
        [InlineData("{\"1\":[0.5]}")]
        [InlineData("{\"1\":[\"0\"]}")]
        [InlineData("{\"1\":[0,0]}")]
        [InlineData("{\"1\":[-1]}")]
        [InlineData("{\"1\":[3]}")]
        [InlineData("{\"1\":[4]}")]
        [InlineData("{\"10\":[0]}")]
        public void RejectsInvalidMaps(string mapping)
        {
            using var fixture = new CliConversionFixture();
            string source = MidiImportCommandsTests.WriteMidi(fixture);
            string map = fixture.PathFor("map.json");
            File.WriteAllText(map, mapping);
            string output = fixture.PathFor("output.json");
            var result = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--channel-map", map, "--json");
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Contains(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray(),
                error => error.GetProperty("code").GetString() == "InvalidChannelMap");
            Assert.Equal(mapping, File.ReadAllText(map));
            Assert.False(File.Exists(output));
            Assert.False(Directory.Exists(output + ".history"));
        }

        /// <summary>map は UTF-8 BOM を許可し、不正 UTF-8 は操作エラー、欠落ファイルは I/O エラー。</summary>
        [Fact]
        public void DistinguishesUtf8AndInputOutputErrors()
        {
            using var fixture = new CliConversionFixture();
            string source = MidiImportCommandsTests.WriteMidi(fixture);
            string map = fixture.PathFor("map.json");
            string output = fixture.PathFor("output.json");
            File.WriteAllBytes(map, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' });
            Assert.Equal(0, CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--channel-map", map, "--dry-run", "--json").ExitCode);
            File.WriteAllBytes(map, new byte[] { 0xC0, 0xAF });
            var invalid = CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--channel-map", map, "--json");
            Assert.Equal(1, invalid.ExitCode);
            using JsonDocument document = JsonDocument.Parse(invalid.Output);
            Assert.Equal("InvalidChannelMap", Assert.Single(document.RootElement.GetProperty("report").GetProperty("errors").EnumerateArray()).GetProperty("code").GetString());
            File.Delete(map);
            Assert.Equal(3, CliConversionFixture.Invoke("import", "midi", source, output, "--chip", "nes", "--channel-map", map, "--json").ExitCode);
            Assert.False(File.Exists(output));
        }
    }
}
