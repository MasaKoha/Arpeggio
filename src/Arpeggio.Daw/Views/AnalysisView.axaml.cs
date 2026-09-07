using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw.Views
{
    /// <summary>解析対象のソロ指定・実行状態・結果テキストを表示する。</summary>
    public partial class AnalysisView : UserControl, IDisposable
    {
        private readonly ComboBox trackSelector;
        private readonly Button analyzeButton;
        private readonly TextBox result;
        private AnalysisPresenter presenter = null!;
        private Song? displayedSong;
        private string[] displayedTracks = Array.Empty<string>();

        /// <summary>解析の固定入力部品を解決する。</summary>
        public AnalysisView()
        {
            AvaloniaXamlLoader.Load(this);
            trackSelector = Require<ComboBox>("TrackSelector");
            analyzeButton = Require<Button>("AnalyzeButton");
            result = Require<TextBox>("AnalysisText");
        }

        /// <summary>解析要求の送信先を接続する。</summary>
        public void Bind(AnalysisPresenter analysisPresenter)
        {
            presenter = analysisPresenter;
            analyzeButton.Click += OnAnalyze;
        }

        /// <summary>全曲と各トラックのソロを選択肢にする。</summary>
        public void ShowTracks(Song song)
        {
            string[] choices = new[] { "全トラック" }
                .Concat(song.Tracks.Select((track, index) => $"ソロ {index}: {track.Name}")).ToArray();
            bool isSameDocument = ReferenceEquals(displayedSong, song);
            if (isSameDocument && displayedTracks.SequenceEqual(choices)) { return; }
            int previousIndex = isSameDocument ? trackSelector.SelectedIndex : 0;
            displayedSong = song;
            displayedTracks = choices;
            trackSelector.ItemsSource = choices;
            trackSelector.SelectedIndex = Math.Clamp(previousIndex, 0, song.Tracks.Count);
        }

        /// <summary>処理中の入力を無効化し、結果を表示する。</summary>
        public void ShowAnalysis(string text, bool isRunning)
        {
            result.Text = text;
            analyzeButton.Content = isRunning ? "解析中…" : "解析";
            analyzeButton.IsEnabled = !isRunning;
            trackSelector.IsEnabled = !isRunning;
        }

        /// <summary>実行イベントを解除する。</summary>
        public void Dispose() => analyzeButton.Click -= OnAnalyze;

        private async void OnAnalyze(object? sender, RoutedEventArgs arguments)
        {
            int? soloTrack = trackSelector.SelectedIndex > 0 ? trackSelector.SelectedIndex - 1 : null;
            await presenter.RunAsync(soloTrack);
        }
        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
    }
}
