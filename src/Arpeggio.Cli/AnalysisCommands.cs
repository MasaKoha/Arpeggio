using System;
using System.CommandLine;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Session;

namespace Arpeggio.Cli
{
    /// <summary>ソングと WAV の読み取り専用音声解析コマンド。</summary>
    internal static class AnalysisCommands
    {
        private const int DefaultWindowMilliseconds = 100;
        private const int DefaultLoopCount = 1;

        internal static Command Create()
        {
            Command command = new Command("analyze", "ソングをレンダリングして音量・周波数・警告を解析");
            // サブコマンド wav に親の必須パスを要求しない。ソング側のアクションで検証する。
            Argument<string?> path = new Argument<string?>("path") { Arity = ArgumentArity.ZeroOrOne };
            Option<int?> track = new Option<int?>("--track");
            Option<int> loops = new Option<int>("--loops") { DefaultValueFactory = _ => DefaultLoopCount };
            Option<int> window = CreateWindowOption();
            Option<bool> json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(track);
            command.Options.Add(loops);
            command.Options.Add(window);
            command.Options.Add(json);
            command.SetAction(result => CliExecution.Run(() =>
            {
                string songPath = result.GetValue(path) ?? throw new ArgumentException("解析するソングのパスを指定してください。");
                EditSession session = CliExecution.Open(songPath);
                AnalysisReport report = AudioAnalysisSource.AnalyzeSong(session.Song!,
                    new AnalysisSettings(WindowMilliseconds: result.GetValue(window)), result.GetValue(track), result.GetValue(loops));
                Write(report, result.GetValue(json));
                return CliExecution.Success;
            }));
            command.Subcommands.Add(CreateWav());
            return command;
        }

        private static Command CreateWav()
        {
            Command command = new Command("wav", "PCM 16 bit モノラル／ステレオ WAV を解析");
            Argument<string> path = new Argument<string>("path");
            Option<int> window = CreateWindowOption();
            Option<bool> json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(window);
            command.Options.Add(json);
            command.SetAction(result => CliExecution.Run(() =>
            {
                AnalysisReport report = AudioAnalysisSource.AnalyzeWav(result.GetValue(path)!,
                    new AnalysisSettings(WindowMilliseconds: result.GetValue(window)));
                Write(report, result.GetValue(json));
                return CliExecution.Success;
            }));
            return command;
        }

        private static Option<int> CreateWindowOption()
        {
            return new Option<int>("--window-ms") { DefaultValueFactory = _ => DefaultWindowMilliseconds };
        }

        private static void Write(AnalysisReport report, bool json)
        {
            Console.WriteLine(json ? SessionOutput.Serialize(report) : AnalysisTextRenderer.Render(report));
        }
    }
}
