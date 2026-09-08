using System;
using System.CommandLine;
using Arpeggio.Codecs;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Render;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>曲の作成・参照・書き出し・履歴コマンドを構成する。</summary>
    internal static class SongCommands
    {
        private const int DefaultTempo = 150;
        private const int DefaultLengthBeats = 16;
        private const int DefaultSampleRate = 44100;
        private const int DefaultLoops = 1;
        private const double DefaultTailSeconds = 0.5;

        internal static Command CreateNew()
        {
            Command command = new Command("new", "ソングを新規作成");
            Argument<string> path = new Argument<string>("path");
            Option<string> chip = new Option<string>("--chip") { Required = true, Description = "nes / gameboy / snes" };
            Option<int> tempo = new Option<int>("--tempo") { DefaultValueFactory = _ => DefaultTempo };
            Option<int> length = new Option<int>("--length-beats") { DefaultValueFactory = _ => DefaultLengthBeats };
            Option<string?> bank = new Option<string?>("--bank") { Description = "SNES: orchestral / band / chip" };
            Option<string?> title = new Option<string?>("--title");
            command.Arguments.Add(path);
            command.Options.Add(chip);
            command.Options.Add(tempo);
            command.Options.Add(length);
            command.Options.Add(title);
            command.Options.Add(bank);
            command.SetAction(result => CliExecution.Run(() =>
            {
                EditSession session = new EditSession();
                session.New(result.GetValue(path)!, ChipReference.ParseChip(result.GetValue(chip)!),
                    result.GetValue(tempo), checked(result.GetValue(length) * Song.FixedTicksPerBeat), result.GetValue(title) ?? string.Empty, SnesBankLayout.Parse(result.GetValue(bank)));
                CliHistoryStore.Save(session);
                Console.WriteLine(session.Path);
                return CliExecution.Success;
            }));
            return command;
        }

        internal static Command CreateInfo()
        {
            Command command = new Command("info", "ソング情報を表示");
            Argument<string> path = new Argument<string>("path");
            Option<bool> json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(json);
            command.SetAction(result => CliExecution.Run(() =>
            {
                EditSession session = CliExecution.Open(result.GetValue(path)!);
                CliHistoryStore.Load(session);
                if (result.GetValue(json))
                {
                    Console.WriteLine(SessionOutput.Serialize(SessionOutput.Info(session)));
                    return CliExecution.Success;
                }
                Song song = session.Song!;
                Console.WriteLine($"{song.Title} | {song.Chip} | {song.TempoBpm} BPM | {song.LengthTicks} ticks | loop {song.LoopStartTick}");
                for (int index = 0; index < song.Tracks.Count; index++)
                {
                    Track track = song.Tracks[index];
                    Console.WriteLine($"track {index}: {track.Name} ({track.Channel}), {track.Notes.Count} notes, muted={track.Muted}, pan={track.Pan}");
                }
                return CliExecution.Success;
            }));
            return command;
        }

        internal static Command CreateShow()
        {
            Command command = new Command("show", "12 tick ごとのトラッカー表示");
            Argument<string> path = new Argument<string>("path");
            Option<int?> track = new Option<int?>("--track");
            Option<int> fromTick = new Option<int>("--from-tick");
            Option<int?> toTick = new Option<int?>("--to-tick");
            Option<bool> json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(track);
            command.Options.Add(fromTick);
            command.Options.Add(toTick);
            command.Options.Add(json);
            command.SetAction(result => CliExecution.Run(() =>
            {
                EditSession session = CliExecution.Open(result.GetValue(path)!);
                Console.WriteLine(result.GetValue(json)
                    ? SessionOutput.Serialize(SessionOutput.Show(session, result.GetValue(track), result.GetValue(fromTick), result.GetValue(toTick)))
                    : SongTextRenderer.Render(session.Song!, result.GetValue(track), result.GetValue(fromTick), result.GetValue(toTick)));
                return CliExecution.Success;
            }));
            return command;
        }

        internal static Command CreateHistory(string name)
        {
            Command command = new Command(name, "履歴を一操作移動");
            Argument<string> path = new Argument<string>("path");
            command.Arguments.Add(path);
            command.SetAction(result => CliExecution.Run(() => CliExecution.Edit(result.GetValue(path)!, session =>
            {
                bool changed = name == "undo" ? session.Undo() : session.Redo();
                if (!changed)
                {
                    throw new InvalidOperationException("対象の編集履歴がありません。");
                }
                Console.WriteLine(SongTextRenderer.Render(session.Song!));
            })));
            return command;
        }

        internal static Command CreateChipReference()
        {
            Command command = new Command("chip-reference", "チャンネル構成・音色・単位を表示");
            Argument<string> chip = new Argument<string>("chip");
            command.Arguments.Add(chip);
            command.SetAction(result => CliExecution.Run(() =>
            {
                Console.WriteLine(ChipReference.Get(ChipReference.ParseChip(result.GetValue(chip)!)));
                return CliExecution.Success;
            }));
            return command;
        }

        internal static Command CreateExport()
        {
            Command command = new Command("export", "音声・チップ演奏を書き出す");
            command.Subcommands.Add(CreateAudioExport("wav"));
            command.Subcommands.Add(CreateAudioExport("ogg"));
            command.Subcommands.Add(ChipExportCommands.Create("nsf"));
            command.Subcommands.Add(ChipExportCommands.Create("vgm"));
            return command;
        }

        private static Command CreateAudioExport(string format)
        {
            const float DefaultQuality = 0.5f;
            Command command = new Command(format, $"ステレオ {format.ToUpperInvariant()} を保存");
            Argument<string> path = new Argument<string>("path");
            Argument<string> output = new Argument<string>("output");
            Option<int> loops = new Option<int>("--loops") { DefaultValueFactory = _ => DefaultLoops };
            Option<int> sampleRate = new Option<int>("--sample-rate") { DefaultValueFactory = _ => DefaultSampleRate };
            Option<double> tail = new Option<double>("--tail") { DefaultValueFactory = _ => DefaultTailSeconds };
            Option<bool> json = new Option<bool>("--json");
            Option<float> quality = new Option<float>("--quality") { DefaultValueFactory = _ => DefaultQuality, Description = "Vorbis VBR 品質（-0.1〜1）" };
            command.Arguments.Add(path);
            command.Arguments.Add(output);
            command.Options.Add(loops);
            command.Options.Add(sampleRate);
            command.Options.Add(tail);
            command.Options.Add(json);
            if (format == "ogg")
            {
                command.Options.Add(quality);
            }
            command.SetAction(result => CliExecution.Run(() =>
            {
                Song song = CliExecution.Open(result.GetValue(path)!).Song!;
                RenderSettings settings = new RenderSettings(result.GetValue(sampleRate), result.GetValue(loops), result.GetValue(tail));
                SongRenderer renderer = new SongRenderer(song, settings);
                float[] samples = renderer.RenderAll();
                string outputPath = result.GetValue(output)!;
                if (format == "ogg")
                {
                    OggWriter.Write(outputPath, samples, settings.SampleRate, result.GetValue(quality));
                }
                else
                {
                    WavWriter.Write(outputPath, samples, settings.SampleRate);
                }
                WriteExportResult(outputPath, renderer.Report, result.GetValue(json));
                return CliExecution.Success;
            }));
            return command;
        }

        private static void WriteExportResult(string path, RenderReport report, bool json)
        {
            if (json)
            {
                Console.WriteLine(SessionOutput.Serialize(new { path, warnings = report.Warnings, report.DroppedWarningCount }));
                return;
            }
            Console.WriteLine(path);
            foreach (RenderWarning warning in report.Warnings)
            {
                Console.Error.WriteLine($"{warning.Kind}: track {warning.TrackIndex}, tick {warning.Tick}, {warning.RequestedValue} -> {warning.ActualValue}");
            }
            if (report.DroppedWarningCount > 0)
            {
                Console.Error.WriteLine($"警告保持上限を超えた通知: {report.DroppedWarningCount}");
            }
        }
    }
}
