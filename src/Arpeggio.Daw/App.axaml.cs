using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Arpeggio.Daw
{
    /// <summary>テーマとデスクトップ lifetime を設定する。</summary>
    public partial class App : Application
    {
        private readonly Func<Window> createWindow;
        private readonly Action<string[]> openFiles;
        private IActivatableLifetime? activatableLifetime;
        private bool isExiting;

        /// <summary>
        /// Avalonia の XAML ランタイムローダー用。実際の起動は Program が引数付きコンストラクタで組み立てるため、
        /// この経路で作られたインスタンスは画面を持たない。
        /// </summary>
        public App() : this(() => throw new InvalidOperationException("Program から画面を組み立ててください。"), _ => { })
        {
        }

        /// <summary>Program の明示的な画面組み立てを受け取る。</summary>
        public App(Func<Window> createWindow, Action<string[]> openFiles)
        {
            this.createWindow = createWindow;
            this.openFiles = openFiles;
            Name = "Arpeggio";
        }
        /// <summary>ダークテーマを読み込む。</summary>
        public override void Initialize() => AvaloniaXamlLoader.Load(this);
        /// <summary>Avalonia 初期化後に Program が画面と Presenter を接続する。</summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Finder から起動した直後の通知も、画面の初期化前に購読して取りこぼさない。
                activatableLifetime = this.TryGetFeature<IActivatableLifetime>();
                if (activatableLifetime is not null)
                {
                    activatableLifetime.Activated += OnActivated;
                }
                desktop.Exit += OnExit;
                desktop.MainWindow = createWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }

        private void OnActivated(object? sender, ActivatedEventArgs arguments)
        {
            if (arguments is FileActivatedEventArgs files)
            {
                string[] paths = GetLocalPaths(files);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!isExiting)
                    {
                        openFiles(paths);
                    }
                });
            }
            else if (arguments is ProtocolActivatedEventArgs protocol && protocol.Uri.IsFile)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!isExiting)
                    {
                        openFiles(new[] { protocol.Uri.LocalPath });
                    }
                });
            }
            else if (arguments.Kind == ActivationKind.Reopen)
            {
                Dispatcher.UIThread.Post(ActivateWindow);
            }
        }

        private static string[] GetLocalPaths(FileActivatedEventArgs arguments)
        {
            try
            {
                string[] paths = new string[arguments.Files.Count];
                for (int fileIndex = 0; fileIndex < paths.Length; fileIndex++)
                {
                    paths[fileIndex] = arguments.Files[fileIndex].TryGetLocalPath() ?? string.Empty;
                }
                return paths;
            }
            finally
            {
                foreach (IStorageItem file in arguments.Files)
                {
                    file.Dispose();
                }
            }
        }

        private void ActivateWindow()
        {
            if (isExiting || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                return;
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        }

        private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs arguments)
        {
            isExiting = true;
            if (activatableLifetime is not null)
            {
                activatableLifetime.Activated -= OnActivated;
            }
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Exit -= OnExit;
            }
        }
    }
}
