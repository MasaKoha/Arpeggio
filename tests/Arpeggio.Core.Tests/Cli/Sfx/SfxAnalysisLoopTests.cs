using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Arpeggio.Core.Tests.Cli.Sfx
{
    /// <summary>AI の編集・解析・tail0 WAV・再解析と、detach の音声保持を公開 CLI で検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxAnalysisLoopTests
    {
        private const int SampleRate = 44100;
        private const double DecibelTolerance = 0.01;

        /// <summary>パラメータ8用途と三チップを、CLIへ渡す独立した名前で列挙する。</summary>
        public static IEnumerable<object[]> Cases()
        {
            string[] chips = { "nes", "gameboy", "snes" };
            string[] presets = { "jump", "coin", "hit", "explosion", "powerup", "laser", "blip", "select" };
            foreach (string chip in chips)
            {
                foreach (string preset in presets)
                {
                    yield return new object[] { chip, preset };
                }
            }
        }

        /// <summary>全24候補を revision 付きで調整し、本体長と16bit量子化の許容差を確認する。</summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void EditablePresetsCompleteAnalysisExportLoop(string chip, string preset)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            string wavePath = fixture.PathFor("source.wav");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path, "--chip", chip, "--preset", preset);
            JsonElement parameters = SfxParameterCommandsTests.AssertSuccess("sfx", "params", path, "--schema");
            JsonElement changed = SfxParameterCommandsTests.AssertSuccess("sfx", "tweak", path, "--slide", "-12",
                "--decay", "0.2", "--expected-revision", parameters.GetProperty("revision").GetString()!);
            double requestedDuration = changed.GetProperty("generation").GetProperty("bodyDurationSeconds").GetDouble();
            byte[] source = File.ReadAllBytes(path);
            byte[] history = File.ReadAllBytes(path + ".history/state.json");
            JsonElement songReport = SfxParameterCommandsTests.AssertSuccess("analyze", path, "--loops", "1", "--window-ms", "20");
            Export(path, wavePath);
            JsonElement waveReport = SfxParameterCommandsTests.AssertSuccess("analyze", "wav", wavePath, "--window-ms", "20");
            Assert.Equal(SampleRate, songReport.GetProperty("sampleRate").GetInt32());
            Assert.Equal(SampleRate, waveReport.GetProperty("sampleRate").GetInt32());
            double duration = songReport.GetProperty("durationSeconds").GetDouble();
            Assert.Equal(duration, waveReport.GetProperty("durationSeconds").GetDouble());
            Assert.InRange(Math.Abs(duration - requestedDuration), 0, 1.0 / SampleRate);
            Assert.InRange(Math.Abs(songReport.GetProperty("rmsDbfs").GetDouble() - waveReport.GetProperty("rmsDbfs").GetDouble()), 0, DecibelTolerance);
            Assert.InRange(Math.Abs(songReport.GetProperty("peakDbfs").GetDouble() - waveReport.GetProperty("peakDbfs").GetDouble()), 0, DecibelTolerance);
            Assert.True(waveReport.GetProperty("rmsDbfs").GetDouble() > -100);
            Assert.Equal(0, songReport.GetProperty("clippedSampleCount").GetInt64());
            Assert.Equal(0, waveReport.GetProperty("clippedSampleCount").GetInt64());
            Assert.Equal(songReport.GetProperty("windows").GetArrayLength(), waveReport.GetProperty("windows").GetArrayLength());
            Assert.Equal(20, waveReport.GetProperty("settings").GetProperty("windowMilliseconds").GetInt32());
            Assert.Equal(source, File.ReadAllBytes(path));
            Assert.Equal(history, File.ReadAllBytes(path + ".history/state.json"));
        }

        /// <summary>detach 前後で三チップの WAV 全バイトが一致し、Undo で定義も戻る。</summary>
        [Theory]
        [InlineData("nes")]
        [InlineData("gameboy")]
        [InlineData("snes")]
        public void DetachKeepsExportedWaveBytes(string chip)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            string beforeWave = fixture.PathFor("before.wav");
            string afterWave = fixture.PathFor("after.wav");
            SfxParameterCommandsTests.AssertSuccess("sfx", "create", path, "--chip", chip, "--preset", "hit");
            byte[] before = File.ReadAllBytes(path);
            Export(path, beforeWave);
            JsonElement detached = SfxParameterCommandsTests.AssertSuccess("sfx", "detach", path);
            Assert.Equal("MissingDefinition", detached.GetProperty("reason").GetString());
            Export(path, afterWave);
            Assert.Equal(File.ReadAllBytes(beforeWave), File.ReadAllBytes(afterWave));
            Assert.Equal(0, CliConversionFixture.Invoke("undo", path).ExitCode);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        private static void Export(string path, string wavePath)
        {
            var export = CliConversionFixture.Invoke("export", "wav", path, wavePath,
                "--loops", "1", "--sample-rate", "44100", "--tail", "0");
            Assert.True(export.ExitCode == 0, export.Error);
        }
    }
}
