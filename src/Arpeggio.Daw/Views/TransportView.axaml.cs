using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw.Views
{
    /// <summary>再生状態の表示とトランスポート入力だけを担当する。</summary>
    public partial class TransportView : UserControl, IDisposable
    {
        private readonly Button playButton;
        private readonly Button stopButton;
        private readonly Button loopButton;
        private readonly TextBox tempoInput;
        private readonly TextBox lengthInput;
        private readonly TextBlock positionLabel;
        private int displayedTempo;
        private int displayedLength;
        /// <summary>表示部品を取得して入力通知を接続する。</summary>
        public TransportView()
        {
            AvaloniaXamlLoader.Load(this);
            playButton = Require<Button>("PlayButton");
            stopButton = Require<Button>("StopButton");
            loopButton = Require<Button>("LoopButton");
            tempoInput = Require<TextBox>("TempoInput");
            lengthInput = Require<TextBox>("LengthInput");
            positionLabel = Require<TextBlock>("PositionLabel");
            playButton.Click += OnPlay;
            stopButton.Click += OnStop;
            loopButton.Click += OnLoop;
            tempoInput.KeyDown += OnTempoKey;
            lengthInput.KeyDown += OnLengthKey;
            tempoInput.LostFocus += OnTempoLostFocus;
            lengthInput.LostFocus += OnLengthLostFocus;
        }
        /// <summary>再生切替要求。</summary>
        public event Action? PlayRequested;
        /// <summary>停止要求。</summary>
        public event Action? StopRequested;
        /// <summary>ループ切替要求。</summary>
        public event Action? LoopRequested;
        /// <summary>Enter で確定されたテンポ文字列。</summary>
        public event Action<string>? TempoSubmitted;
        /// <summary>Enter で確定された長さ文字列。</summary>
        public event Action<string>? LengthSubmitted;
        /// <summary>入力途中の文字列を保ち、再生表示を更新する。</summary>
        public void Show(bool isPlaying, bool isLooping, int tempoBpm, int lengthTicks, string position)
        {
            displayedTempo = tempoBpm;
            displayedLength = lengthTicks;
            playButton.Content = isPlaying ? "❚❚ 停止" : "▶ 再生";
            loopButton.Content = isLooping ? "ループ ON" : "ループ OFF";
            if (!tempoInput.IsKeyboardFocusWithin) { tempoInput.Text = tempoBpm.ToString(CultureInfo.InvariantCulture); }
            if (!lengthInput.IsKeyboardFocusWithin) { lengthInput.Text = lengthTicks.ToString(CultureInfo.InvariantCulture); }
            positionLabel.Text = position;
        }
        /// <summary>入力購読を解除する。</summary>
        public void Dispose()
        {
            playButton.Click -= OnPlay;
            stopButton.Click -= OnStop;
            loopButton.Click -= OnLoop;
            tempoInput.KeyDown -= OnTempoKey;
            lengthInput.KeyDown -= OnLengthKey;
            tempoInput.LostFocus -= OnTempoLostFocus;
            lengthInput.LostFocus -= OnLengthLostFocus;
        }
        private TControl Require<TControl>(string name) where TControl : Control =>
            this.FindControl<TControl>(name) ?? throw new InvalidOperationException($"{name} がありません。");
        private void OnPlay(object? sender, RoutedEventArgs arguments) => PlayRequested?.Invoke();
        private void OnStop(object? sender, RoutedEventArgs arguments) => StopRequested?.Invoke();
        private void OnLoop(object? sender, RoutedEventArgs arguments) => LoopRequested?.Invoke();
        private void OnTempoLostFocus(object? sender, RoutedEventArgs arguments) => tempoInput.Text = displayedTempo.ToString(CultureInfo.InvariantCulture);
        private void OnLengthLostFocus(object? sender, RoutedEventArgs arguments) => lengthInput.Text = displayedLength.ToString(CultureInfo.InvariantCulture);
        private void OnTempoKey(object? sender, KeyEventArgs arguments)
        {
            if (arguments.Key != Key.Enter) { return; }
            TempoSubmitted?.Invoke(tempoInput.Text ?? string.Empty);
            arguments.Handled = true;
        }
        private void OnLengthKey(object? sender, KeyEventArgs arguments)
        {
            if (arguments.Key != Key.Enter) { return; }
            LengthSubmitted?.Invoke(lengthInput.Text ?? string.Empty);
            arguments.Handled = true;
        }
    }
}
