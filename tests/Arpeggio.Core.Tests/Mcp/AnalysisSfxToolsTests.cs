using System;
using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Tests.Sfx;
using Arpeggio.Core.Tests.Sfx.Presets;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp
{
    /// <summary>MCP の四つの追加ツールと共有セッションの非破壊解析を検証する。</summary>
    public sealed class AnalysisSfxToolsTests
    {
        /// <summary>全プリセットを生成・書き出し・解析し、MCP の JSON 契約を検証する。</summary>
        [Theory]
        [MemberData(nameof(SfxPresetFactoryTests.Cases), MemberType = typeof(SfxPresetFactoryTests))]
        public void NewSfx_ExportsAndAnalyzesAllPresets(ChipKind chip, SfxPresetKind kind, double minimum, double maximum)
        {
            WithTools((tools, session, directory) =>
            {
                string path = Path.Combine(directory, "effect.arpeggio.json");
                string wavePath = Path.Combine(directory, "effect.wav");
                RequireSuccess(tools.NewSfx(path, SfxPresetCatalog.Get(kind).Name, chip.ToString().ToLowerInvariant(), "effect"));
                Assert.Equal(path, session.Path);
                Assert.Equal("effect", session.Song!.Title);
                Assert.Equal(0, session.History.UndoCount);
                RequireSuccess(tools.ExportWav(wavePath, tail: 0));
                JsonElement report = RequireSuccess(tools.AnalyzeWav(wavePath, windowMs: 25));
                Assert.InRange(report.GetProperty("durationSeconds").GetDouble(), minimum, maximum);
                Assert.True(report.GetProperty("rmsDbfs").GetDouble() > -60);
                Assert.Equal(0, report.GetProperty("clippedSampleCount").GetInt64());
                Assert.Equal(0, report.GetProperty("renderWarnings").GetArrayLength());
                JsonElement songReport = RequireSuccess(tools.AnalyzeSong());
                Assert.Equal(report.GetProperty("durationSeconds").GetDouble(), songReport.GetProperty("durationSeconds").GetDouble(), 8);
            });
        }

        /// <summary>ソロ解析はミュート状態・ファイル・参照・履歴を変更しない。</summary>
        [Fact]
        public void AnalyzeSong_KeepsSessionAndReturnsSynthesisWarnings()
        {
            WithTools((tools, session, directory) =>
            {
                string path = Path.Combine(directory, "song.arpeggio.json");
                RequireSuccess(tools.NewSong(path, "nes", lengthBeats: 1));
                RequireSuccess(tools.AddInstrument("{\"id\":2,\"name\":\"triangle\",\"kind\":\"NesTriangle\"}"));
                RequireSuccess(tools.AddNote(2, 0, 48, "C4", volume: 7, instrumentId: 2));
                RequireSuccess(tools.ApplyOperations("[{\"kind\":\"SetTrackMuted\",\"track\":2,\"muted\":true}]"));
                Song original = session.Song!;
                string before = File.ReadAllText(path);
                int undoBefore = session.History.UndoCount;
                JsonElement report = RequireSuccess(tools.AnalyzeSong(track: 2, loops: 2, windowMs: 50));
                Assert.Equal(0.8, report.GetProperty("durationSeconds").GetDouble(), 8);
                Assert.Equal("TriangleVolumeIgnored", report.GetProperty("renderWarnings")[0].GetProperty("kind").GetString());
                Assert.Equal(before, File.ReadAllText(path));
                Assert.Same(original, session.Song);
                Assert.True(session.Song!.Tracks[2].Muted);
                Assert.Equal(undoBefore, session.History.UndoCount);
            });
        }

        /// <summary>一覧と WAV 解析は未オープンで利用でき、エラー時もセッションを維持する。</summary>
        [Fact]
        public void Tools_ValidateArgumentsAndKeepSessionOnFailure()
        {
            WithTools((tools, session, directory) =>
            {
                Assert.Equal(8, RequireSuccess(tools.SfxPresets()).GetArrayLength());
                RequireError(tools.AnalyzeSong(), 1);
                RequireError(tools.AnalyzeWav(Path.Combine(directory, "missing.wav")), 3);
                Assert.Null(session.Song);
                string path = Path.Combine(directory, "effect.arpeggio.json");
                RequireError(tools.NewSfx(path, "unknown"), 1);
                Assert.False(File.Exists(path));
                RequireSuccess(tools.NewSfx(path, "blip"));
                Assert.Equal(ChipKind.Nes, session.Song!.Chip);
                Song original = session.Song!;
                string before = File.ReadAllText(path);
                RequireError(tools.NewSfx(path, "coin"), 1);
                RequireError(tools.AnalyzeSong(track: -1), 1);
                RequireError(tools.AnalyzeSong(loops: 0), 1);
                RequireError(tools.AnalyzeSong(windowMs: -1), 1);
                string malformed = Path.Combine(directory, "malformed.wav");
                File.WriteAllText(malformed, "invalid");
                RequireError(tools.AnalyzeWav(malformed), 1);
                Assert.Same(original, session.Song);
                Assert.Equal(before, File.ReadAllText(path));
                string wavePath = Path.Combine(directory, "blip.wav");
                RequireSuccess(tools.ExportWav(wavePath, tail: 0));
                ArpeggioTools unopened = new ArpeggioTools(new EditSession());
                Assert.True(RequireSuccess(unopened.AnalyzeWav(wavePath)).GetProperty("rmsDbfs").GetDouble() > -60);
            });
        }

        private static JsonElement RequireSuccess(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                Assert.False(document.RootElement.TryGetProperty("error", out _), json);
            }
            return document.RootElement.Clone();
        }

        private static void RequireError(string json, int exitCode)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal(exitCode, document.RootElement.GetProperty("exitCode").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("error").GetString()));
        }

        private static void WithTools(Action<ArpeggioTools, EditSession, string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-analysis-mcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                EditSession session = new EditSession();
                action(new ArpeggioTools(session), session, directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
