using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Session;
using ModelContextProtocol.Server;

namespace Arpeggio.Mcp
{
    /// <summary>共有する編集セッションを MCP ツールとして公開する。</summary>
    [McpServerToolType]
    public sealed class ArpeggioTools
    {
        private const int DefaultTempo = 150;
        private const int DefaultLengthBeats = 16;
        private const int DefaultVolume = 15;
        private const int DefaultLoopCount = 1;
        private const int DefaultSampleRate = 44100;
        private const double DefaultTailSeconds = 0.5;
        private const int StereoChannelCount = 2;
        private const int OperationError = 1;
        private const int DocumentError = 2;
        private const int InputOutputError = 3;
        private readonly EditSession session;

        /// <summary>ホストが共有するセッションを受け取る。</summary>
        public ArpeggioTools(EditSession session)
        {
            this.session = session;
        }

        /// <summary>新規ソングを作成して保存する。</summary>
        [McpServerTool(Name = "new_song", ReadOnly = false, Destructive = false)]
        [Description("新規ソングを作成して保存する。既存ファイルの上書きは拒否する。")]
        public string NewSong(
            [Description("保存先 .arpeggio.json")] string path,
            [Description("nes / gameboy / snes")] string chip,
            [Description("毎分の四分音符数")] int tempo = DefaultTempo,
            [Description("四分音符単位の曲の長さ")] int lengthBeats = DefaultLengthBeats,
            [Description("曲名")] string? title = null)
        {
            return Invoke(() =>
            {
                int lengthTicks = checked(lengthBeats * Song.FixedTicksPerBeat);
                session.New(path, ChipReference.ParseChip(chip), tempo, lengthTicks, title ?? string.Empty);
                return SessionOutput.Info(session);
            });
        }

        /// <summary>ソングを開いてセッション履歴を初期化する。</summary>
        [McpServerTool(Name = "open_song", ReadOnly = false, Destructive = false)]
        [Description("ソングを開く。セッション内の undo / redo 履歴は初期化する。")]
        public string OpenSong([Description("入力 .arpeggio.json")] string path)
        {
            return Invoke(() =>
            {
                session.Open(path);
                return SessionOutput.Info(session);
            });
        }

        /// <summary>開いているソングを現在のパスに保存する。</summary>
        [McpServerTool(Name = "save_song", ReadOnly = false, Destructive = false)]
        [Description("開いているソングを保存する。編集ツールの成功時にも自動保存する。")]
        public string SaveSong()
        {
            return Invoke(() =>
            {
                session.Save();
                return new { path = session.Path };
            });
        }

        /// <summary>曲名・チップ・テンポ・トラック・音色の情報を返す。</summary>
        [McpServerTool(Name = "song_info", ReadOnly = true, Destructive = false)]
        [Description("開いているソングの情報を JSON で返す。")]
        public string SongInfo()
        {
            return Invoke(() => SessionOutput.Info(session));
        }

        /// <summary>指定区間をトラッカー風テキストまたは JSON で返す。</summary>
        [McpServerTool(Name = "show_song", ReadOnly = true, Destructive = false)]
        [Description("縦が tick、横がチャンネルのトラッカー表示。1 行は 12 tick。json=true ならノートの正確な値を返す。")]
        public string ShowSong(
            [Description("0 始まりのトラック番号。省略すると全トラック")] int? track = null,
            [Description("開始 tick。範囲に含む")] int fromTick = 0,
            [Description("終了 tick。範囲に含まない。省略すると曲末尾")] int? toTick = null,
            [Description("JSON 形式で返す")] bool json = false)
        {
            return Invoke(() =>
            {
                if (json)
                {
                    return SessionOutput.Show(session, track, fromTick, toTick);
                }
                return SongTextRenderer.Render(GetSong(), track, fromTick, toTick);
            });
        }

        /// <summary>ノートを追加して自動保存する。</summary>
        [McpServerTool(Name = "add_note", ReadOnly = false, Destructive = false)]
        [Description("ノートを追加する。同じトラック内の時間重複は不可。四分音符は 48 tick。")]
        public string AddNote(
            [Description("0 始まりのトラック番号")] int track,
            [Description("開始 tick")] int tick,
            [Description("長さ tick")] int durationTicks,
            [Description("C5 / C#5 / Db5 / 60。MIDI 60 = C4")] string note,
            [Description("音量 0〜15")] int volume = DefaultVolume,
            [Description("既存の音色 ID。省略時はトラックのチャンネルに合う最初の音色")] int? instrumentId = null,
            [Description("効果の JSON 配列。例: [{\"kind\":\"PitchSlide\",\"value\":-4}]")] string? effects = null)
        {
            return Invoke(() =>
            {
                Note added = new Note
                {
                    Tick = tick,
                    DurationTicks = durationTicks,
                    MidiNote = NoteName.Parse(note),
                    Volume = volume,
                    InstrumentId = DefaultInstrumentResolver.Resolve(GetSong(), track, instrumentId),
                    Effects = ParseEffects(effects) ?? Array.Empty<NoteEffect>()
                };
                session.Notes.Add(track, added);
                return new { track, note = added };
            });
        }

        /// <summary>指定開始 tick のノートを削除して自動保存する。</summary>
        [McpServerTool(Name = "remove_note", ReadOnly = false, Destructive = false)]
        [Description("指定した開始 tick のノートを削除する。")]
        public string RemoveNote(
            [Description("0 始まりのトラック番号")] int track,
            [Description("削除するノートの開始 tick")] int tick)
        {
            return Invoke(() =>
            {
                session.Notes.Remove(track, tick);
                return new { track, tick, removed = true };
            });
        }

        /// <summary>ノートの指定項目を一つの履歴操作で変更する。</summary>
        [McpServerTool(Name = "update_note", ReadOnly = false, Destructive = false)]
        [Description("開始 tick でノートを特定し、指定した項目だけ変更する。移動・長さ変更も一度に適用できる。")]
        public string UpdateNote(
            [Description("0 始まりのトラック番号")] int track,
            [Description("変更前の開始 tick")] int tick,
            [Description("変更後の開始 tick")] int? toTick = null,
            [Description("変更後の長さ tick")] int? durationTicks = null,
            [Description("C5 / C#5 / Db5 / 60。MIDI 60 = C4")] string? note = null,
            [Description("変更後の音量 0〜15")] int? volume = null,
            [Description("変更後の音色 ID")] int? instrumentId = null,
            [Description("効果の JSON 配列。[] で全解除、省略で維持")] string? effects = null)
        {
            return Invoke(() =>
            {
                BatchOperation operation = new BatchOperation
                {
                    Kind = BatchOperationKind.UpdateNote,
                    Track = track,
                    Tick = tick,
                    ToTick = toTick,
                    DurationTicks = durationTicks,
                    MidiNote = note is null ? null : NoteName.Parse(note),
                    Volume = volume,
                    InstrumentId = instrumentId,
                    Effects = ParseEffects(effects)
                };
                BatchOperationApplier.Apply(session, new[] { operation });
                return new { track, tick = toTick ?? tick, updated = true };
            });
        }

        /// <summary>JSON 操作列を一つの履歴として原子的に適用する。</summary>
        [McpServerTool(Name = "apply_operations", ReadOnly = false, Destructive = false)]
        [Description("JSON 配列を 1 回の履歴で適用し自動保存する。失敗時は全件取り消す。kind: AddNote / RemoveNote / MoveNote / ResizeNote / UpdateNote / AddInstrument / UpdateInstrument / RemoveInstrument / SetTempo / SetLength / SetLoopStart / SetTrackMuted / SetTrackPan。")]
        public string ApplyOperations(
            [Description("操作 JSON 配列。例: [{\"kind\":\"AddNote\",\"track\":0,\"tick\":0,\"durationTicks\":24,\"midiNote\":60,\"volume\":15,\"instrumentId\":1,\"effects\":[]}]")] string operations,
            [Description("各操作で track を省略した場合のトラック番号")] int? track = null)
        {
            return Invoke(() =>
            {
                BatchOperation[] parsedOperations = BatchOperationJson.Deserialize(operations);
                BatchOperationApplier.Apply(session, parsedOperations, track);
                return new { applied = parsedOperations.Length, song = SessionOutput.Info(session) };
            });
        }

        /// <summary>JSON で指定した音色を追加する。</summary>
        [McpServerTool(Name = "add_instrument", ReadOnly = false, Destructive = false)]
        [Description("音色 JSON を追加して自動保存する。id は未使用の正整数を指定する。")]
        public string AddInstrument(
            [Description(".arpeggio.json の instruments[] 要素と同じ JSON。例: {\"id\":2,\"name\":\"lead\",\"kind\":\"NesPulse\",\"duty\":\"Percent50\"}")] string instrument)
        {
            return Invoke(() =>
            {
                Instrument added = InstrumentJson.Deserialize(instrument);
                session.Instruments.Add(added);
                return new { instrument = added };
            });
        }

        /// <summary>JSON 内の ID に一致する音色を置き換える。</summary>
        [McpServerTool(Name = "update_instrument", ReadOnly = false, Destructive = false)]
        [Description("音色 JSON 全体で既存音色を置き換えて自動保存する。省略した項目にはその音色 kind の既定値を使う。")]
        public string UpdateInstrument(
            [Description(".arpeggio.json の instruments[] 要素と同じ JSON。id で既存音色を指定")] string instrument)
        {
            return Invoke(() =>
            {
                Instrument replacement = InstrumentJson.Deserialize(instrument);
                session.Instruments.Update(replacement);
                return new { instrument = replacement };
            });
        }

        /// <summary>未参照の音色を削除する。</summary>
        [McpServerTool(Name = "remove_instrument", ReadOnly = false, Destructive = false)]
        [Description("指定 ID の音色を削除する。ノートから参照中の音色は削除できない。")]
        public string RemoveInstrument([Description("削除する音色 ID")] int instrumentId)
        {
            return Invoke(() =>
            {
                session.Instruments.Remove(instrumentId);
                return new { instrumentId, removed = true };
            });
        }

        /// <summary>ステレオ WAV を書き出し、補正警告を JSON に含める。</summary>
        [McpServerTool(Name = "export_wav", ReadOnly = false, Destructive = false)]
        [Description("16 bit PCM ステレオ WAV を書き出す。音域補正などの警告があっても成功し、warnings に補正箇所を返す。")]
        public string ExportWav(
            [Description("出力 WAV パス")] string path,
            [Description("初回の全曲再生を含む再生回数")] int loops = DefaultLoopCount,
            [Description("サンプルレート Hz")] int sampleRate = DefaultSampleRate,
            [Description("末尾の残響用余白、秒")] double tail = DefaultTailSeconds)
        {
            return Invoke(() =>
            {
                RenderSettings settings = new RenderSettings(sampleRate, loops, tail);
                SongRenderer renderer = new SongRenderer(GetSong(), settings);
                float[] samples = renderer.RenderAll();
                WavWriter.Write(path, samples, sampleRate);
                return new
                {
                    path,
                    sampleRate,
                    channels = StereoChannelCount,
                    frames = samples.Length / StereoChannelCount,
                    warnings = renderer.Report.Warnings,
                    droppedWarningCount = renderer.Report.DroppedWarningCount
                };
            });
        }

        /// <summary>履歴を一操作戻して保存する。</summary>
        [McpServerTool(Name = "undo", ReadOnly = false, Destructive = false)]
        [Description("セッションの編集履歴を一操作戻して保存する。履歴がなければ changed=false。")]
        public string Undo()
        {
            return Invoke(() => new { changed = session.Undo(), song = SessionOutput.Info(session) });
        }

        /// <summary>履歴を一操作やり直して保存する。</summary>
        [McpServerTool(Name = "redo", ReadOnly = false, Destructive = false)]
        [Description("セッションの編集履歴を一操作やり直して保存する。履歴がなければ changed=false。")]
        public string Redo()
        {
            return Invoke(() => new { changed = session.Redo(), song = SessionOutput.Info(session) });
        }

        /// <summary>チップの制約・音色・効果・単位の説明を返す。</summary>
        [McpServerTool(Name = "chip_reference", ReadOnly = true, Destructive = false)]
        [Description("チャンネル構成、音域、音量、音色パラメータ、エフェクト、マクロ単位を説明する。ソングを開かず呼び出せる。")]
        public string GetChipReference([Description("nes / gameboy / snes")] string chip)
        {
            return Invoke(() => ChipReference.Get(ChipReference.ParseChip(chip)));
        }

        private Song GetSong()
        {
            return session.Song ?? throw new InvalidOperationException("new_song または open_song でソングを開いてください。");
        }

        private static NoteEffect[]? ParseEffects(string? effects)
        {
            if (effects is null)
            {
                return null;
            }
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return JsonSerializer.Deserialize<NoteEffect[]>(effects, options)
                ?? throw new ArgumentException("effects は JSON 配列で指定してください。", nameof(effects));
        }

        private string Invoke(Func<object> action)
        {
            // ツールごとのインスタンスが異なっても、共有するソングと履歴の競合を防ぐ。
            lock (session)
            {
                return InvokeLocked(action);
            }
        }

        private static string InvokeLocked(Func<object> action)
        {
            try
            {
                object result = action();
                return result is string text ? text : SessionOutput.Serialize(result);
            }
            catch (SongValidationException exception)
            {
                return SessionOutput.Serialize(new { error = exception.Message, exitCode = DocumentError });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return SessionOutput.Serialize(new { error = exception.Message, exitCode = InputOutputError });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                FormatException or OverflowException or JsonException)
            {
                return SessionOutput.Serialize(new { error = exception.Message, exitCode = OperationError });
            }
        }
    }
}
