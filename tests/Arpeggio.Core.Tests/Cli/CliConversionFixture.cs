using System;
using System.IO;
using Arpeggio.Cli;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>プロセス内 CLI の出力捕捉と、テストごとの独立した作業ファイルを管理する。</summary>
    internal sealed class CliConversionFixture : IDisposable
    {
        internal CliConversionFixture()
        {
            DirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "arpeggio-conversion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }
        internal string PathFor(string name) => Path.Combine(DirectoryPath, name);

        internal string CreateSong(ChipKind chip = ChipKind.Nes)
        {
            const int Tempo = 120;
            const int LengthTicks = 96;
            const int NoteTicks = 48;
            const int Pitch = 69;
            string path = PathFor("source.arpeggio.json");
            Song song = SongFactory.Create(chip, Tempo, LengthTicks);
            song.Title = "Test song";
            song.Tracks[0].Notes.Add(new Note { DurationTicks = NoteTicks, MidiNote = Pitch });
            SongSerializer.Save(song, path);
            return path;
        }

        internal static (int ExitCode, string Output, string Error) Invoke(params string[] arguments)
        {
            TextWriter originalOutput = Console.Out;
            TextWriter originalError = Console.Error;
            using var output = new StringWriter();
            using var error = new StringWriter();
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

        /// <summary>このテストが作成したファイルだけを削除する。</summary>
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
