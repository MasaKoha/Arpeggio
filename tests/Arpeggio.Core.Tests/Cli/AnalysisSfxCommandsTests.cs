using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Cli;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Tests.Sfx;
using Arpeggio.Core.Tests.Sfx.Presets;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>追加 CLI の実際の解析・保存・書き出しコマンド体系を検証する。</summary>
    [Collection("Cli")]
    public sealed class AnalysisSfxCommandsTests
    {
        /// <summary>全 24 組み合わせを一コマンドで生成し、WAV 書き出しと解析まで接続する。</summary>
        [Theory]
        [MemberData(nameof(SfxPresetFactoryTests.Cases), MemberType = typeof(SfxPresetFactoryTests))]
        public void SfxNew_ExportsAndAnalyzesAllPresets(ChipKind chip, SfxPresetKind kind, double minimum, double maximum)
        {
            WithDirectory(directory =>
            {
                string path = Path.Combine(directory, "effect.arpeggio.json");
                string wavePath = Path.Combine(directory, "effect.wav");
                string preset = SfxPresetCatalog.Get(kind).Name;
                RequireSuccess("sfx", "new", path, "--preset", preset, "--chip", chip.ToString().ToLowerInvariant(), "--title", "test effect");
                Song song = SongSerializer.Load(path);
                Assert.Equal("test effect", song.Title);
                Assert.Equal(chip, song.Chip);
                RequireSuccess("export", "wav", path, wavePath, "--tail", "0");
                string output = RequireSuccess("analyze", "wav", wavePath, "--window-ms", "25", "--json");
                using JsonDocument document = JsonDocument.Parse(output);
                JsonElement report = document.RootElement;
                Assert.InRange(report.GetProperty("durationSeconds").GetDouble(), minimum, maximum);
                Assert.True(report.GetProperty("rmsDbfs").GetDouble() > -60);
                Assert.Equal(0, report.GetProperty("clippedSampleCount").GetInt64());
                Assert.NotEqual(0, report.GetProperty("windows").GetArrayLength());
                Assert.Equal(25, report.GetProperty("settings").GetProperty("windowMilliseconds").GetInt32());
                string before = File.ReadAllText(path);
                string historyPath = Path.Combine(path + ".history", "state.json");
                string historyBefore = File.ReadAllText(historyPath);
                RequireSuccess("analyze", path, "--json");
                Assert.Equal(before, File.ReadAllText(path));
                Assert.Equal(historyBefore, File.ReadAllText(historyPath));
            });
        }

        /// <summary>ソロ・ループ・合成警告が解析結果に反映され、元ファイルは不変。</summary>
        [Fact]
        public void AnalyzeSong_ReportsSoloLoopsAndRenderWarnings()
        {
            WithDirectory(directory =>
            {
                string path = Path.Combine(directory, "song.arpeggio.json");
                Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 48);
                song.Instruments.Add(new NesTriangleInstrument { Id = 2 });
                song.Tracks[0].Notes.Add(new Note { MidiNote = 69 });
                song.Tracks[2].Notes.Add(new Note { MidiNote = 60, Volume = 7, InstrumentId = 2 });
                song.Tracks[2].Muted = true;
                SongSerializer.Save(song, path);
                string before = File.ReadAllText(path);
                string output = RequireSuccess("analyze", path, "--track", "2", "--loops", "2", "--window-ms", "50", "--json");
                using JsonDocument document = JsonDocument.Parse(output);
                JsonElement report = document.RootElement;
                Assert.Equal(0.8, report.GetProperty("durationSeconds").GetDouble(), 8);
                Assert.Equal(16, report.GetProperty("windows").GetArrayLength());
                Assert.Equal("TriangleVolumeIgnored", report.GetProperty("renderWarnings")[0].GetProperty("kind").GetString());
                Assert.Equal(before, File.ReadAllText(path));
                string text = RequireSuccess("analyze", path, "--track", "2");
                Assert.Contains("合成 TriangleVolumeIgnored", text);
                Assert.Contains("開始 s | RMS dBFS", text);
                Assert.True(text.IndexOf("長さ:", StringComparison.Ordinal) < text.IndexOf("警告:", StringComparison.Ordinal));
            });
        }

        /// <summary>既定チップ・プリセット一覧・不正入力・上書き拒否を公開コマンドから確認する。</summary>
        [Fact]
        public void Commands_ValidateArgumentsAndProtectExistingFiles()
        {
            WithDirectory(directory =>
            {
                string path = Path.Combine(directory, "effect.arpeggio.json");
                string list = RequireSuccess("sfx", "list");
                foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
                {
                    Assert.Contains(preset.Name + ":", list);
                }
                RequireSuccess("sfx", "new", path, "--preset", "jump");
                Assert.Equal(ChipKind.Nes, SongSerializer.Load(path).Chip);
                string before = File.ReadAllText(path);
                Assert.Equal(1, Invoke("sfx", "new", path, "--preset", "coin").ExitCode);
                Assert.Equal(before, File.ReadAllText(path));
                Assert.Equal(1, Invoke("sfx", "new", Path.Combine(directory, "invalid.json"), "--preset", "missing").ExitCode);
                Assert.Equal(1, Invoke("sfx", "new", Path.Combine(directory, "invalid.json")).ExitCode);
                Assert.Equal(1, Invoke("analyze").ExitCode);
                Assert.Equal(1, Invoke("analyze", path, "--track", "99").ExitCode);
                Assert.Equal(1, Invoke("analyze", path, "--loops", "0").ExitCode);
                Assert.Equal(1, Invoke("analyze", path, "--window-ms", "0").ExitCode);
                Assert.Equal(3, Invoke("analyze", "wav", Path.Combine(directory, "missing.wav")).ExitCode);
                string malformed = Path.Combine(directory, "malformed.wav");
                File.WriteAllText(malformed, "invalid");
                Assert.Equal(1, Invoke("analyze", "wav", malformed).ExitCode);
            });
        }

        private static string RequireSuccess(params string[] arguments)
        {
            (int exitCode, string output, string error) = Invoke(arguments);
            Assert.True(exitCode == 0, error);
            return output;
        }

        private static (int ExitCode, string Output, string Error) Invoke(params string[] arguments)
        {
            TextWriter originalOutput = Console.Out;
            TextWriter originalError = Console.Error;
            using StringWriter output = new StringWriter();
            using StringWriter error = new StringWriter();
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                int exitCode = CliExecution.Run(arguments);
                return (exitCode, output.ToString(), error.ToString());
            }
            finally
            {
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }
        }

        private static void WithDirectory(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-analysis-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
