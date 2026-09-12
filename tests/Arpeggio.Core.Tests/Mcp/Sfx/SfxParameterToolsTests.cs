using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Cli;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp.Sfx
{
    /// <summary>新ツールの入力・schema・全パラメータと CLI の同一結果を検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxParameterToolsTests
    {
        private const string CommonPatch = "\"tone\":{\"enabled\":true,\"baseFrequencyHz\":330.1234564," +
            "\"slideSemitonesPerSecond\":-12,\"deltaSlideSemitonesPerSecondSquared\":24," +
            "\"vibratoDepthCents\":10,\"vibratoSpeedHz\":4.5,\"pitchChangeSemitones\":7," +
            "\"pitchChangeTimeSeconds\":0.05,\"repeatPeriodSeconds\":0.1," +
            "\"envelope\":{\"volume\":10,\"attackSeconds\":0.02,\"sustainSeconds\":0.1,\"decaySeconds\":0.2,\"punch\":0.2}}," +
            "\"noise\":{\"enabled\":true,\"envelope\":{\"volume\":9,\"attackSeconds\":0.03," +
            "\"sustainSeconds\":0.1,\"decaySeconds\":0.3,\"punch\":0.1}}";

        /// <summary>seed と置換要求を必須にし、JSON文字列の入力名を既存クライアントへ固定する。</summary>
        [Fact]
        public void RequiredParametersAndJsonInputsRemainDiscoverable()
        {
            AssertParameters(nameof(ArpeggioTools.CreateSfx), new[] { "path", "chip", "preset", "title", "dryRun" }, 1);
            AssertParameters(nameof(ArpeggioTools.SfxParameters), new[] { "includeSchema", "chip" }, 0);
            AssertParameters(nameof(ArpeggioTools.TweakSfx), new[] { "parameters", "expectedRevision", "dryRun" }, 1);
            AssertParameters(nameof(ArpeggioTools.RandomizeSfx), new[] { "category", "seed", "expectedRevision", "dryRun" }, 2);
            AssertParameters(nameof(ArpeggioTools.MutateSfx), new[] { "seed", "strength", "locks", "expectedRevision", "dryRun" }, 1);
            AssertParameters(nameof(ArpeggioTools.RegenerateSfx), new[] { "replaceGenerated", "expectedRevision", "dryRun" }, 1);
            AssertParameters(nameof(ArpeggioTools.DetachSfx), new[] { "expectedRevision", "dryRun" }, 0);
            Assert.Equal(typeof(string), typeof(ArpeggioTools).GetMethod(nameof(ArpeggioTools.TweakSfx))!.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(string), typeof(ArpeggioTools).GetMethod(nameof(ArpeggioTools.MutateSfx))!.GetParameters()[2].ParameterType);
        }

        /// <summary>未オープンで chip を指定すると、CLI と同じ初期値・可否・全項目を非破壊で取得できる。</summary>
        [Theory]
        [InlineData("nes")]
        [InlineData("gameboy")]
        [InlineData("snes")]
        public void UnopenedSchemaAndPresetsMatchCli(string chip)
        {
            using var fixture = new SfxToolFixture();
            SfxToolFixture.Failure(fixture.Tools.SfxParameters(), 1, "InvalidParameter", "chip");
            SfxToolFixture.Failure(fixture.Tools.SfxParameters(includeSchema: true), 1, "InvalidParameter", "chip");
            string parameters = fixture.Tools.SfxParameters(chip: chip);
            JsonElement defaults = SfxToolFixture.Success(parameters);
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("revision").ValueKind);
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("candidateRevision").ValueKind);
            AssertCliEquals(parameters, "sfx", "params", "--chip", chip);
            AssertCliEquals(fixture.Tools.SfxParameterPresets(), "sfx", "list", "--editable");
            Assert.Equal(SessionOutput.Serialize(SfxPresetCatalog.GetAll()), fixture.Tools.SfxPresets());
            Assert.Null(fixture.Session.Song);
            Assert.Null(fixture.Session.Path);
            Assert.Empty(Directory.GetFileSystemEntries(fixture.DirectoryPath));
        }

        /// <summary>全共通値と全チップ固有値の patch が CLI と同じ保存バイト・診断・revision になる。</summary>
        [Theory]
        [InlineData("nes", "\"nes\":{\"dutyPercent\":50,\"dutySweepPercentPerSecond\":20,\"noisePeriodIndex\":5,\"noiseMode\":\"short\",\"noiseSlideIndicesPerSecond\":-10}")]
        [InlineData("gameboy", "\"gameBoy\":{\"dutyPercent\":75,\"dutySweepPercentPerSecond\":-25,\"noiseSelection\":100,\"noiseWidth\":7,\"noiseSlideSelectionsPerSecond\":20}")]
        [InlineData("snes", "\"snes\":{\"waveform\":\"sine\",\"noiseRate\":20}")]
        public void CompletePatchMatchesCliAndCore(string chip, string chipPatch)
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.PathFor("current.json");
            string commandPath = fixture.PathFor("command.json");
            AssertCliEquals(fixture.Tools.CreateSfx(path, chip), "sfx", "create", commandPath, "--chip", chip);
            SfxToolFixture.Success(fixture.Tools.OpenSong(path));
            string patch = "{" + CommonPatch + "," + chipPatch + "}";
            string patchPath = fixture.PathFor("patch.json");
            File.WriteAllText(patchPath, patch);
            SfxParameters original = fixture.Session.Song!.Sfx!.Known!.Parameters;
            SfxParameterPatchResult expected = SfxParameterPatch.Apply(original, fixture.Session.Song.Chip, patch);
            AssertCliEquals(fixture.Tools.TweakSfx(patch), "sfx", "tweak", commandPath, "--patch", patchPath);
            Assert.Equal(expected.Parameters, fixture.Session.Song.Sfx!.Known!.Parameters);
            Assert.Equal(File.ReadAllBytes(commandPath), File.ReadAllBytes(path));
            Assert.Equal(1, fixture.Session.History.UndoCount);
            AssertCliEquals(fixture.Tools.SfxParameters(includeSchema: true, chip: chip), "sfx", "params", commandPath, "--schema");
            Assert.False(SfxToolFixture.Success(fixture.Tools.SfxParameters()).TryGetProperty("schema", out _));
        }

        /// <summary>現在曲の表示をディスクから読み直さず、異なるチップ指定を拒否する。</summary>
        [Fact]
        public void ParametersUseCurrentSongWithoutSavingIt()
        {
            using var fixture = new SfxToolFixture();
            string path = fixture.CreateAndOpen("gameboy");
            byte[] originalBytes = File.ReadAllBytes(path);
            fixture.Session.Song!.Title = "メモリ内の曲名";
            JsonElement result = SfxToolFixture.Success(fixture.Tools.SfxParameters());
            Assert.Equal(SfxHash.ComputeRevision(fixture.Session.Song), result.GetProperty("revision").GetString());
            SfxToolFixture.Failure(fixture.Tools.SfxParameters(chip: "nes"), 1, "UnsupportedParameter", "chip");
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Equal(0, fixture.Session.History.UndoCount);
        }

        internal static void AssertCliEquals(string json, params string[] arguments)
        {
            SfxToolFixture.Success(json);
            var result = CliConversionFixture.Invoke(arguments.Concat(new[] { "--json" }).ToArray());
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Equal(json, result.Output.TrimEnd('\r', '\n'));
        }

        private static void AssertParameters(string methodName, string[] names, int requiredCount)
        {
            ParameterInfo[] parameters = typeof(ArpeggioTools).GetMethod(methodName)!.GetParameters();
            Assert.Equal(names, parameters.Select(parameter => parameter.Name));
            Assert.All(parameters.Take(requiredCount), parameter => Assert.False(parameter.HasDefaultValue));
            Assert.All(parameters.Skip(requiredCount), parameter => Assert.True(parameter.HasDefaultValue));
        }
    }
}
