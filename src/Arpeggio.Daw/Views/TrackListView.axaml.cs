using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Arpeggio.Daw.Views
{
    /// <summary>トラックの表示と選択・ミュート入力を通知する。</summary>
    public partial class TrackListView : UserControl, IDisposable
    {
        private const double TrackNameWidth = 118;
        private const double RowSpacing = 6;
        private readonly StackPanel rows;
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<CheckBox> muteBoxes = new List<CheckBox>();
        private bool isRefreshing;
        /// <summary>XAML の表示先を取得する。</summary>
        public TrackListView()
        {
            AvaloniaXamlLoader.Load(this);
            rows = this.FindControl<StackPanel>("Rows") ?? throw new InvalidOperationException("Rows がありません。");
        }
        /// <summary>選択されたトラック番号。</summary>
        public event Action<int>? TrackSelected;
        /// <summary>ミュート切替を要求されたトラック番号。</summary>
        public event Action<int>? MuteRequested;
        /// <summary>既存行を再利用して名前・選択・ミュートを表示する。</summary>
        public void ShowTracks(IReadOnlyList<Track> tracks, int selectedTrack)
        {
            isRefreshing = true;
            try
            {
                if (buttons.Count != tracks.Count) { CreateRows(tracks.Count); }
                for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
                {
                    buttons[trackIndex].Content = tracks[trackIndex].Name;
                    buttons[trackIndex].FontWeight = trackIndex == selectedTrack ? FontWeight.Bold : FontWeight.Normal;
                    buttons[trackIndex].Foreground = trackIndex == selectedTrack ? Brushes.Aquamarine : Brushes.LightGray;
                    muteBoxes[trackIndex].IsChecked = tracks[trackIndex].Muted;
                }
            }
            finally { isRefreshing = false; }
        }
        /// <summary>動的な行のイベント購読を解除する。</summary>
        public void Dispose() => ClearRows();
        private void CreateRows(int count)
        {
            ClearRows();
            for (int trackIndex = 0; trackIndex < count; trackIndex++)
            {
                Button button = new Button { Tag = trackIndex, Width = TrackNameWidth };
                CheckBox muteBox = new CheckBox { Tag = trackIndex, Content = "M" };
                ToolTip.SetTip(muteBox, "ミュート");
                button.Click += OnSelected;
                muteBox.IsCheckedChanged += OnMuteChanged;
                buttons.Add(button);
                muteBoxes.Add(muteBox);
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = RowSpacing };
                row.Children.Add(button);
                row.Children.Add(muteBox);
                rows.Children.Add(row);
            }
        }
        private void OnSelected(object? sender, RoutedEventArgs arguments)
        {
            if (!isRefreshing && sender is Button { Tag: int trackIndex }) { TrackSelected?.Invoke(trackIndex); }
        }
        private void OnMuteChanged(object? sender, RoutedEventArgs arguments)
        {
            if (!isRefreshing && sender is CheckBox { Tag: int trackIndex }) { MuteRequested?.Invoke(trackIndex); }
        }
        private void ClearRows()
        {
            foreach (Button button in buttons) { button.Click -= OnSelected; }
            foreach (CheckBox muteBox in muteBoxes) { muteBox.IsCheckedChanged -= OnMuteChanged; }
            buttons.Clear();
            muteBoxes.Clear();
            rows.Children.Clear();
        }
    }
}
