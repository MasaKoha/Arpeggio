using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Views;
using Avalonia;

namespace Arpeggio.Daw
{
    /// <summary>DAW の依存関係を明示的に組み立てる起動点。</summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] arguments)
        {
            if (arguments.Length != 1)
            {
                Console.Error.WriteLine("使い方: arpeggio-daw <path.arpeggio.json>");
                return 1;
            }
            using DawDocument document = new DawDocument();
            using PlaybackEngine playback = new PlaybackEngine(new SdlAudioOutput());
            MainWindow? window = null;
            try
            {
                document.Open(arguments[0]);
                return AppBuilder.Configure(() => new App(() =>
                {
                    window = new MainWindow();
                    MainWindowPresenter presenter = new MainWindowPresenter(window, document, playback);
                    window.Bind(presenter, document.Path);
                    presenter.Open(document.Path);
                    return window;
                })).UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(arguments);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                SongValidationException or ArgumentException or InvalidOperationException)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
            finally { window?.Dispose(); }
        }
    }
}
