using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw
{
    /// <summary>テーマとデスクトップ lifetime を設定する。</summary>
    public partial class App : Application
    {
        private readonly Func<Window> createWindow;
        /// <summary>Program の明示的な画面組み立てを受け取る。</summary>
        public App(Func<Window> createWindow) { this.createWindow = createWindow; }
        /// <summary>ダークテーマを読み込む。</summary>
        public override void Initialize() => AvaloniaXamlLoader.Load(this);
        /// <summary>Avalonia 初期化後に Program が画面と Presenter を接続する。</summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = createWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
