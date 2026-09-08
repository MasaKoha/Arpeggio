using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Views;
using Avalonia;
using Avalonia.Controls;
#if AVALON
using Avalon;
using Arpeggio.Daw.Diagnostics;
#endif

namespace Arpeggio.Daw
{
    /// <summary>DAW の依存関係を明示的に組み立てる起動点。</summary>
    internal static class Program
    {
        private const string SongExtension = ".arpeggio.json";
        private const string WelcomeFileName = "welcome.arpeggio.json";

        [STAThread]
        private static int Main(string[] arguments)
        {
            if (arguments.Length > 1)
            {
                Console.Error.WriteLine("使い方: arpeggio-daw [path.arpeggio.json]");
                return 1;
            }
            using DawDocument document = new DawDocument();
            using PlaybackEngine playback = new PlaybackEngine(new SdlAudioOutput());
            MainWindow? window = null;
            MainWindowPresenter? presenter = null;
#if AVALON
            AvalonDawIntegration? avalonIntegration = null;
#endif
            try
            {
                document.Open(arguments.Length == 1 ? arguments[0] : GetWelcomePath());
                return AppBuilder.Configure(() => new App(() =>
                {
                    window = new MainWindow();
                    presenter = new MainWindowPresenter(window, document, playback);
                    window.Bind(presenter, document.Path);
                    presenter.Open(document.Path);
#if AVALON
                    avalonIntegration?.AttachWindow(window);
#endif
                    return window;
                }, paths =>
                {
                    if (window is not null && presenter is not null)
                    {
                        OpenRequestedFiles(window, presenter, paths);
                    }
                })).UsePlatformDetect().WithInterFont().LogToTrace()
#if AVALON
                    .UseAvalon(onStarted: host =>
                    {
                        // AfterSetup は画面生成より先に走るため、状態は観測時に解決する。
                        avalonIntegration = new AvalonDawIntegration(host, document, () => presenter, () => window);
                        if (window is not null)
                        {
                            avalonIntegration.AttachWindow(window);
                        }
                    })
#endif
                    .StartWithClassicDesktopLifetime(arguments);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                SongValidationException or ArgumentException or InvalidOperationException)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
            finally
            {
#if AVALON
                avalonIntegration?.Dispose();
#endif
                window?.Dispose();
            }
        }

        private static string GetWelcomePath()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arpeggio");
            string path = Path.Combine(directory, WelcomeFileName);
            Directory.CreateDirectory(directory);
            if (!File.Exists(path))
            {
                // .app 内を保存先にすると署名が壊れるため、編集可能なデモはユーザー領域に置く。
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Examples", "snes-demo.arpeggio.json"), path);
            }
            return path;
        }

        private static void OpenRequestedFiles(MainWindow window, MainWindowPresenter presenter, string[] paths)
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
            presenter.Execute(() =>
            {
                if (paths.Length != 1)
                {
                    throw new ArgumentException("曲ファイルは一つずつ開いてください。");
                }
                string path = paths[0];
                if (!path.EndsWith(SongExtension, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(".arpeggio.json のローカルファイルを選択してください。");
                }
                path = Path.GetFullPath(path);
                if (path == presenter.DocumentPath)
                {
                    return;
                }
                presenter.PianoRoll.EndDrag();
                if (presenter.IsDirty)
                {
                    throw new InvalidOperationException("未保存の編集があります。保存してから、曲ファイルをもう一度開いてください。");
                }
                presenter.Open(path);
            });
        }
    }
}
