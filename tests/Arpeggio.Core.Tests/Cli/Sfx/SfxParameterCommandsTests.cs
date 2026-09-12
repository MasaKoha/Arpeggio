using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Sfx
{
    /// <summary>全パラメータの個別指定と patch、旧入口、読み取り専用表示を検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxParameterCommandsTests
    {
        private const string CommonPatch = "\"tone\":{\"enabled\":true,\"baseFrequencyHz\":330.1234564," +
            "\"slideSemitonesPerSecond\":-12,\"deltaSlideSemitonesPerSecondSquared\":24," +
            "\"vibratoDepthCents\":10,\"vibratoSpeedHz\":4.5,\"pitchChangeSemitones\":7," +
            "\"pitchChangeTimeSeconds\":0.05,\"repeatPeriodSeconds\":0.1," +
            "\"envelope\":{\"volume\":10,\"attackSeconds\":0.02,\"sustainSeconds\":0.1,\"decaySeconds\":0.2,\"punch\":0.2}}," +
            "\"noise\":{\"enabled\":true,\"envelope\":{\"volume\":9,\"attackSeconds\":0.03," +
            "\"sustainSeconds\":0.1,\"decaySeconds\":0.3,\"punch\":0.1}}";

        /// <summary>全共通値と全チップ固有値が、カルチャに依存せず同じ保存バイト列になる。</summary>
        [Theory]
        [InlineData("nes", "\"nes\":{\"dutyPercent\":50,\"dutySweepPercentPerSecond\":20,\"noisePeriodIndex\":5,\"noiseMode\":\"short\",\"noiseSlideIndicesPerSecond\":-10}", "--duty 50 --duty-sweep 20 --noise-period 5 --noise-mode short --noise-slide -10")]
        [InlineData("gameboy", "\"gameBoy\":{\"dutyPercent\":75,\"dutySweepPercentPerSecond\":-25,\"noiseSelection\":100,\"noiseWidth\":7,\"noiseSlideSelectionsPerSecond\":20}", "--duty 75 --duty-sweep -25 --noise-selection 100 --noise-width 7 --noise-slide 20")]
        [InlineData("snes", "\"snes\":{\"waveform\":\"sine\",\"noiseRate\":20}", "--waveform sine --noise-rate 20")]
        public void EveryParameterMatchesNestedPatch(string chip, string chipPatch, string chipOptions)
        {
            using var fixture = new CliConversionFixture();
            string individualPath = fixture.PathFor("individual.json");
            string patchPath = fixture.PathFor("patched.json");
            AssertSuccess("sfx", "create", individualPath, "--chip", chip);
            File.Copy(individualPath, patchPath);
            string patchFile = fixture.PathFor("patch.json");
            File.WriteAllText(patchFile, "{" + CommonPatch + "," + chipPatch + "}");
            string options = "--tone-enabled true --noise-enabled true --frequency 330.1234564 --slide -12 " +
                "--delta-slide 24 --vibrato-depth 10 --vibrato-speed 4.5 --pitch-change 7 --pitch-change-time 0.05 " +
                "--repeat-period 0.1 --volume 10 --attack 0.02 --sustain 0.1 --decay 0.2 --punch 0.2 " +
                "--noise-volume 9 --noise-attack 0.03 --noise-sustain 0.1 --noise-decay 0.3 --noise-punch 0.1 " + chipOptions;
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                commaCulture.NumberFormat.NumberDecimalSeparator = ",";
                commaCulture.NumberFormat.NumberGroupSeparator = ".";
                CultureInfo.CurrentCulture = commaCulture;
                AssertSuccess(new[] { "sfx", "tweak", individualPath }.Concat(options.Split(' ')).ToArray());
                AssertSuccess("sfx", "tweak", patchPath, "--patch", patchFile);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
            Assert.Equal(File.ReadAllBytes(individualPath), File.ReadAllBytes(patchPath));
            Song song = SongSerializer.Load(individualPath);
            Assert.Equal(330.123456, song.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
            Assert.True(SfxSynchronization.Inspect(song).Editable);
        }

        /// <summary>stdin patch、要求時だけの schema、再読込の revision と保存不変を固定する。</summary>
        [Fact]
        public void StdinPatchAndReadOnlyParametersPreserveFiles()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            AssertSuccess("sfx", "create", path);
            TextReader originalInput = Console.In;
            using var input = new StringReader("{\"tone\":{\"baseFrequencyHz\":550}}");
            try
            {
                Console.SetIn(input);
                AssertSuccess("sfx", "tweak", path, "--patch", "-");
            }
            finally
            {
                Console.SetIn(originalInput);
            }
            byte[] songBytes = File.ReadAllBytes(path);
            byte[] historyBytes = File.ReadAllBytes(path + ".history/state.json");
            JsonElement output = AssertSuccess("sfx", "params", path);
            Assert.False(output.TryGetProperty("schema", out _));
            Assert.Equal(SfxHash.ComputeRevision(SongSerializer.Load(path)), output.GetProperty("revision").GetString());
            Assert.Equal(550, output.GetProperty("parameters").GetProperty("tone").GetProperty("baseFrequencyHz").GetDouble());
            JsonElement schema = AssertSuccess("sfx", "params", path, "--schema").GetProperty("schema");
            Assert.Equal(32, schema.GetArrayLength());
            Assert.Equal(25, schema.EnumerateArray().Count(item => item.GetProperty("supported").GetBoolean()));
            Assert.Equal(songBytes, File.ReadAllBytes(path));
            Assert.Equal(historyBytes, File.ReadAllBytes(path + ".history/state.json"));
            JsonElement defaults = AssertSuccess("sfx", "params", "--chip", "snes");
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("revision").ValueKind);
            Assert.Equal(440, defaults.GetProperty("parameters").GetProperty("tone").GetProperty("baseFrequencyHz").GetDouble());
            Assert.Equal(32, defaults.GetProperty("schema").GetArrayLength());
        }

        /// <summary>旧一覧の全文と new の保存結果を維持し、新一覧は8用途×3チップを返す。</summary>
        [Fact]
        public void LegacyCommandsRemainUnchangedAndEditableListIsComplete()
        {
            using var fixture = new CliConversionFixture();
            var listing = CliConversionFixture.Invoke("sfx", "list");
            Assert.Equal(0, listing.ExitCode);
            string expected = string.Concat(SfxPresetCatalog.GetAll().Select(preset => $"{preset.Name}: {preset.Description}{Environment.NewLine}"));
            Assert.Equal(expected, listing.Output);
            string path = fixture.PathFor("legacy.json");
            Assert.Equal(0, CliConversionFixture.Invoke("sfx", "new", path, "--preset", "jump").ExitCode);
            Assert.Equal(SongSerializer.Serialize(SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.Jump)), File.ReadAllText(path));
            Assert.Equal("MissingDefinition", AssertSuccess("sfx", "params", path).GetProperty("reason").GetString());
            JsonElement presets = AssertSuccess("sfx", "list", "--editable").GetProperty("presets");
            Assert.Equal(8, presets.GetArrayLength());
            Assert.All(presets.EnumerateArray(), preset => Assert.Equal(3, preset.GetProperty("defaults").GetArrayLength()));
        }

        /// <summary>通常表示は警告を stderr、JSON表示は同じ診断を stdout の一オブジェクトに含める。</summary>
        [Fact]
        public void WarningOutputUsesTheSelectedChannel()
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            AssertSuccess("sfx", "create", path);
            var text = CliConversionFixture.Invoke("sfx", "tweak", path, "--volume", "0");
            Assert.Equal(0, text.ExitCode);
            Assert.Contains("SilentParameters", text.Error);
            Assert.DoesNotContain("SilentParameters", text.Output);
            JsonElement result = AssertSuccess("sfx", "params", path);
            Assert.Contains(result.GetProperty("warnings").EnumerateArray(),
                warning => warning.GetProperty("code").GetString() == "SilentParameters");
        }

        internal static JsonElement AssertSuccess(params string[] arguments)
        {
            var result = CliConversionFixture.Invoke(arguments.Concat(new[] { "--json" }).ToArray());
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            return document.RootElement.Clone();
        }
    }
}
