using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Analysis;
using Arpeggio.Mcp;
using ModelContextProtocol.Server;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp
{
    /// <summary>stdio ホストを起動せず、MCP の公開契約と共有セッション操作を検証する。</summary>
    public sealed class ArpeggioToolsTests
    {
        /// <summary>全 16 ツールが指定名と string 戻り値で公開され、構造化出力を指定しない。</summary>
        [Fact]
        public void ExposesAllToolNamesWithoutStructuredContent()
        {
            string[] expectedNames =
            {
                "new_song", "open_song", "save_song", "song_info", "show_song", "add_note", "remove_note", "update_note",
                "apply_operations", "add_instrument", "update_instrument", "remove_instrument", "export_wav", "undo", "redo", "chip_reference"
            };
            Assert.NotNull(typeof(ArpeggioTools).GetCustomAttribute<McpServerToolTypeAttribute>());
            MethodInfo[] methods = typeof(ArpeggioTools).GetMethods()
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null).ToArray();
            string[] actualNames = methods.Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!).ToArray();
            Assert.Equal(expectedNames.OrderBy(name => name), actualNames.OrderBy(name => name));
            foreach (MethodInfo method in methods)
            {
                Assert.Equal(typeof(string), method.ReturnType);
                McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
                Assert.False(attribute.UseStructuredContent);
                CustomAttributeData declaration = method.CustomAttributes.Single(candidate => candidate.AttributeType == typeof(McpServerToolAttribute));
                Assert.DoesNotContain(declaration.NamedArguments, argument => argument.MemberName == nameof(McpServerToolAttribute.UseStructuredContent));
            }
        }

        /// <summary>別ツールインスタンスも同じセッションを表示・保存し、再オープンで履歴を初期化する。</summary>
        [Fact]
        public void CreatesSavesShowsAndOpensSharedSong()
        {
            WithTools((tools, session, directory) =>
            {
                string path = Path.Combine(directory, "song.arpeggio.json");
                JsonElement created = RequireSuccess(tools.NewSong(path, "nes", tempo: 120, lengthBeats: 4, title: "theme"));
                Assert.Equal("theme", created.GetProperty("title").GetString());
                Assert.Equal("Nes", created.GetProperty("chip").GetString());
                Assert.Equal(192, created.GetProperty("lengthTicks").GetInt32());
                ArpeggioTools anotherInstance = new(session);
                Assert.Equal(120, RequireSuccess(anotherInstance.SongInfo()).GetProperty("tempoBpm").GetInt32());
                RequireSuccess(tools.AddNote(0, 0, 24, "C5"));
                string text = anotherInstance.ShowSong(track: 0, fromTick: 0, toTick: 36);
                Assert.Contains("C-5 15 01", text);
                Assert.Contains("...", text);
                Assert.Contains("---", text);
                JsonElement shown = RequireSuccess(anotherInstance.ShowSong(track: 0, fromTick: 0, toTick: 36, json: true));
                Assert.Equal(72, shown.GetProperty("tracks")[0].GetProperty("notes")[0].GetProperty("midiNote").GetInt32());
                Assert.Equal(path, RequireSuccess(anotherInstance.SaveSong()).GetProperty("path").GetString());
                JsonElement reopened = RequireSuccess(anotherInstance.OpenSong(path));
                Assert.Equal(0, reopened.GetProperty("undoCount").GetInt32());
                Assert.Single(session.Song!.Tracks[0].Notes);
                Assert.Equal("theme", SongSerializer.Load(path).Title);
            });
        }

        /// <summary>チップ説明はドキュメントを開かず共有 Core の文字列をそのまま返す。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes)]
        [InlineData("gameboy", ChipKind.GameBoy)]
        [InlineData("snes", ChipKind.Snes)]
        public void ReturnsSharedChipReference(string name, ChipKind chip)
        {
            ArpeggioTools tools = new(new EditSession());
            Assert.Equal(ChipReference.Get(chip), tools.GetChipReference(name));
        }

        /// <summary>音名と効果を解釈し、複数項目のノート更新を一履歴として保存する。</summary>
        [Fact]
        public void AddsUpdatesAndRemovesNotes()
        {
            WithTools((tools, session, directory) =>
            {
                string path = Path.Combine(directory, "song.arpeggio.json");
                RequireSuccess(tools.NewSong(path, "nes"));
                RequireSuccess(tools.AddNote(0, 0, 24, "Db5", volume: 12,
                    effects: "[{\"kind\":\"PitchSlide\",\"value\":-4}]"));
                Note original = Assert.Single(session.Song!.Tracks[0].Notes);
                Assert.Equal(73, original.MidiNote);
                Assert.Equal(NoteEffectKind.PitchSlide, Assert.Single(original.Effects).Kind);
                int beforeUpdate = session.History.UndoCount;
                RequireSuccess(tools.UpdateNote(0, 0, toTick: 48, durationTicks: 36, note: "60", volume: 9));
                Note updated = Assert.Single(session.Song!.Tracks[0].Notes);
                Assert.Equal(48, updated.Tick);
                Assert.Equal(36, updated.DurationTicks);
                Assert.Equal(60, updated.MidiNote);
                Assert.Equal(9, updated.Volume);
                Assert.Single(updated.Effects);
                Assert.Equal(beforeUpdate + 1, session.History.UndoCount);
                RequireSuccess(tools.UpdateNote(0, 48, effects: "[]"));
                Assert.Empty(Assert.Single(SongSerializer.Load(path).Tracks[0].Notes).Effects);
                RequireSuccess(tools.RemoveNote(0, 48));
                Assert.Empty(SongSerializer.Load(path).Tracks[0].Notes);
            });
        }

        /// <summary>保存形式の音色 JSON を任意の kind 位置で追加・置換し、参照制約を守る。</summary>
        [Fact]
        public void AddsReplacesAndRemovesInstrumentJson()
        {
            WithTools((tools, session, directory) =>
            {
                RequireSuccess(tools.NewSong(Path.Combine(directory, "song.arpeggio.json"), "nes"));
                JsonElement result = RequireSuccess(tools.AddInstrument("{\"id\":2,\"name\":\"lead\",\"kind\":\"NesPulse\",\"duty\":\"Percent25\",\"volumeMacro\":{\"values\":[15,12],\"loopIndex\":1}}"));
                JsonElement added = result.GetProperty("instrument");
                Assert.Equal(2, added.GetProperty("id").GetInt32());
                Assert.Equal("lead", added.GetProperty("name").GetString());
                Assert.Equal("NesPulse", added.GetProperty("kind").GetString());
                RequireSuccess(tools.UpdateInstrument("{\"id\":2,\"name\":\"replacement\",\"kind\":\"NesPulse\",\"duty\":\"Percent75\"}"));
                NesPulseInstrument instrument = Assert.IsType<NesPulseInstrument>(session.Song!.Instruments[1]);
                Assert.Equal("replacement", instrument.Name);
                Assert.Equal(DutyCycle.Percent75, instrument.Duty);
                Assert.Null(instrument.VolumeMacro);
                RequireSuccess(tools.AddNote(0, 0, 24, "C4", instrumentId: 2));
                RequireError(tools.RemoveInstrument(2), 1);
                RequireSuccess(tools.RemoveNote(0, 0));
                RequireSuccess(tools.RemoveInstrument(2));
                Assert.Single(session.Song!.Instruments);
            });
        }

        /// <summary>バッチは一度の undo/redo で戻り、不正 JSON と途中失敗は全状態を保持する。</summary>
        [Fact]
        public void AppliesBatchAsOneHistoryEntryAndRollsBackFailures()
        {
            WithTools((tools, session, directory) =>
            {
                string path = Path.Combine(directory, "song.arpeggio.json");
                RequireSuccess(tools.NewSong(path, "nes"));
                const string Operations = "[{\"kind\":\"AddNote\",\"tick\":0,\"durationTicks\":24,\"midiNote\":60},{\"kind\":\"AddNote\",\"tick\":24,\"durationTicks\":24,\"midiNote\":64},{\"kind\":\"SetTempo\",\"tempoBpm\":180}]";
                Assert.Equal(3, RequireSuccess(tools.ApplyOperations(Operations, track: 0)).GetProperty("applied").GetInt32());
                Assert.Equal(1, session.History.UndoCount);
                Assert.Equal(2, session.Song!.Tracks[0].Notes.Count);
                Assert.Equal(180, session.Song!.TempoBpm);
                Assert.True(RequireSuccess(tools.Undo()).GetProperty("changed").GetBoolean());
                Assert.Empty(session.Song!.Tracks[0].Notes);
                Assert.Equal(150, session.Song!.TempoBpm);
                Assert.True(RequireSuccess(tools.Redo()).GetProperty("changed").GetBoolean());
                Assert.Equal(2, session.Song!.Tracks[0].Notes.Count);
                string beforeFailure = File.ReadAllText(path);
                RequireError(tools.ApplyOperations("{"), 1);
                RequireError(tools.ApplyOperations("[{\"kind\":\"SetTempo\",\"tempoBpm\":200},{\"kind\":\"RemoveNote\",\"track\":0,\"tick\":999}]"), 1);
                Assert.Equal(beforeFailure, File.ReadAllText(path));
                Assert.Equal(180, session.Song!.TempoBpm);
                Assert.Equal(1, session.History.UndoCount);
            });
        }

        /// <summary>補正警告があっても WAV を成功として書き出し、警告箇所を返す。</summary>
        [Fact]
        public void ExportsAudibleWavWithWarnings()
        {
            WithTools((tools, session, directory) =>
            {
                RequireSuccess(tools.NewSong(Path.Combine(directory, "song.arpeggio.json"), "nes", lengthBeats: 1));
                RequireSuccess(tools.AddInstrument("{\"id\":2,\"name\":\"bass\",\"kind\":\"NesTriangle\"}"));
                RequireSuccess(tools.AddNote(2, 0, 48, "C4", volume: 7, instrumentId: 2));
                string outputPath = Path.Combine(directory, "song.wav");
                JsonElement exported = RequireSuccess(tools.ExportWav(outputPath, sampleRate: 22050, tail: 0));
                Assert.Equal(outputPath, exported.GetProperty("path").GetString());
                Assert.Equal(22050, exported.GetProperty("sampleRate").GetInt32());
                Assert.Equal(2, exported.GetProperty("channels").GetInt32());
                JsonElement warning = exported.GetProperty("warnings").EnumerateArray()
                    .Single(value => value.GetProperty("kind").GetString() == "TriangleVolumeIgnored");
                Assert.Equal(2, warning.GetProperty("trackIndex").GetInt32());
                Assert.Equal(0, warning.GetProperty("tick").GetInt32());
                Assert.Equal(7, warning.GetProperty("requestedValue").GetDouble());
                using FileStream stream = File.OpenRead(outputPath);
                WavReader reader = new(stream);
                Assert.Equal(22050, reader.SampleRate);
                Assert.Contains(reader.Samples, sample => sample != 0);
                Assert.Equal(exported.GetProperty("frames").GetInt32() * 2, reader.Samples.Length);
                Assert.Equal(2, session.Song!.Instruments.Count);
            });
        }

        /// <summary>例外を外へ出さず、引数・ドキュメント・I/O の分類を JSON に維持する。</summary>
        [Fact]
        public void ConvertsErrorsToJsonExitCodes()
        {
            WithTools((tools, session, directory) =>
            {
                RequireError(tools.SongInfo(), 1);
                RequireError(tools.SaveSong(), 1);
                RequireError(tools.GetChipReference("unknown"), 1);
                RequireError(tools.OpenSong(Path.Combine(directory, "missing.json")), 3);
                string invalidPath = Path.Combine(directory, "invalid.json");
                File.WriteAllText(invalidPath, "{}");
                RequireError(tools.OpenSong(invalidPath), 2);
                Assert.Null(session.Song);
                RequireSuccess(tools.NewSong(Path.Combine(directory, "song.arpeggio.json"), "nes"));
                RequireError(tools.AddNote(0, 0, 24, "invalid"), 1);
                RequireError(tools.AddNote(0, 0, 24, "C4", effects: "{"), 1);
                RequireError(tools.AddNote(0, 0, 24, "C4", volume: 16), 2);
                RequireError(tools.AddInstrument("{"), 1);
                RequireError(tools.UpdateInstrument("{\"id\":1,\"name\":\"lead\",\"kind\":\"NesPulse\",\"unknown\":true}"), 1);
                Assert.Empty(session.Song!.Tracks[0].Notes);
                Assert.Equal(0, session.History.UndoCount);
            });
        }

        private static JsonElement RequireSuccess(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("error", out _), json);
            return document.RootElement.Clone();
        }

        private static void RequireError(string json, int exitCode)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("error").GetString()));
            Assert.Equal(exitCode, document.RootElement.GetProperty("exitCode").GetInt32());
        }

        private static void WithTools(Action<ArpeggioTools, EditSession, string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-mcp-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                EditSession session = new();
                action(new ArpeggioTools(session), session, directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
