using System;
using Arpeggio.Daw.Presenters;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw.Views
{
    /// <summary>スナップ単位の選択を通知する。</summary>
    public partial class PianoRollToolbarView : UserControl, IDisposable
    {
        private readonly ComboBox snapInput;
        private readonly SnapResolution[] resolutions =
        {
            SnapResolution.Bar, SnapResolution.Half, SnapResolution.Quarter, SnapResolution.Eighth,
            SnapResolution.Sixteenth, SnapResolution.Triplet, SnapResolution.None
        };
        /// <summary>スナップ選択部品を初期化する。</summary>
        public PianoRollToolbarView()
        {
            AvaloniaXamlLoader.Load(this);
            snapInput = this.FindControl<ComboBox>("SnapInput") ?? throw new InvalidOperationException("SnapInput がありません。");
            snapInput.ItemsSource = new[] { "1/1", "1/2", "1/4", "1/8", "1/16", "1/3（3 連）", "なし" };
            snapInput.SelectedIndex = Array.IndexOf(resolutions, SnapResolution.Sixteenth);
            snapInput.SelectionChanged += OnSelectionChanged;
        }
        /// <summary>選択されたスナップ単位。</summary>
        public event Action<SnapResolution>? SnapChanged;
        /// <summary>入力購読を解除する。</summary>
        public void Dispose() => snapInput.SelectionChanged -= OnSelectionChanged;
        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs arguments)
        {
            if (snapInput.SelectedIndex >= 0) { SnapChanged?.Invoke(resolutions[snapInput.SelectedIndex]); }
        }
    }
}
