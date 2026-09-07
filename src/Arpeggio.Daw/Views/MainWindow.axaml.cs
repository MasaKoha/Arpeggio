using System;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Watch;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Arpeggio.Daw.Views
{
    /// <summary>表示・入力・UI スレッドへの通知を Presenter へ接続する。</summary>
    public partial class MainWindow : Window, IMainWindowView, IDisposable
    {
        private const int DisplayIntervalMilliseconds = 33;
        private readonly PianoRollControl pianoRoll;
        private readonly KeyboardStripControl keyboard;
        private readonly TimeRulerControl ruler;
        private readonly ScrollViewer rollScroll;
        private readonly TrackListView tracks;
        private readonly InstrumentPanelView instruments;
        private readonly TransportView transport;
        private readonly TextBlock statusLabel;
        private readonly Button warningsButton;
        private readonly TextBox warningsText;
        private readonly DispatcherTimer displayTimer = new DispatcherTimer();
        private MainWindowPresenter? presenter;
        private Action pollPlayback = null!;
        private SongFileWatcher? watcher;
        private Song? song;
        private bool isDisposed;
        private bool warningsVisible;
        private int displayedWarningCount = -1;

        /// <summary>XAML の表示部品だけを取得する。</summary>
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            pianoRoll = Require<PianoRollControl>("PianoRoll");
            keyboard = Require<KeyboardStripControl>("Keyboard");
            ruler = Require<TimeRulerControl>("Ruler");
            rollScroll = Require<ScrollViewer>("RollScroll");
            tracks = Require<TrackListView>("Tracks");
            instruments = Require<InstrumentPanelView>("Instruments");
            transport = Require<TransportView>("Transport");
            statusLabel = Require<TextBlock>("StatusLabel");
            warningsButton = Require<Button>("WarningsButton");
            warningsText = Require<TextBox>("WarningsText");
        }
        /// <summary>Program が組み立てた Presenter と明示的に結線する。</summary>
        public void Bind(MainWindowPresenter mainPresenter, string path)
        {
            presenter = mainPresenter;
            pollPlayback = mainPresenter.Poll;
            pianoRoll.Bind(mainPresenter.PianoRoll, mainPresenter.Execute);
            instruments.Bind(mainPresenter.Instruments, mainPresenter.Execute);
            tracks.TrackSelected += OnTrackSelected;
            tracks.MuteRequested += OnMuteRequested;
            transport.PlayRequested += OnPlay;
            transport.StopRequested += OnStop;
            transport.LoopRequested += OnLoop;
            transport.TempoSubmitted += OnTempo;
            transport.LengthSubmitted += OnLength;
            pianoRoll.ZoomChanged += OnZoom;
            rollScroll.ScrollChanged += OnScroll;
            SizeChanged += OnSizeChanged;
            warningsButton.Click += OnWarnings;
            Opened += OnOpened;
            Closed += OnClosed;
            AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
            displayTimer.Interval = TimeSpan.FromMilliseconds(DisplayIntervalMilliseconds);
            displayTimer.Tick += OnDisplayTick;
            watcher = new SongFileWatcher(path, () => Dispatcher.UIThread.Post(OnExternalChange));
        }
        /// <summary>トラック・ノート・音色の表示を更新する。</summary>
        public void ShowSong(Song current, int selectedTrack, int? selectedTick)
        {
            song = current;
            Title = $"Arpeggio — {current.Title}";
            tracks.ShowTracks(current.Tracks, selectedTrack);
            pianoRoll.ShowSong(current, selectedTrack, selectedTick);
            instruments.Refresh();
            SynchronizeViewport();
        }
        /// <summary>タイマー通知で合成せず、再生状態とカーソルだけを表示する。</summary>
        public void ShowTransport(bool isPlaying, bool isLooping, int tempoBpm, int lengthTicks, double positionTick, string position)
        {
            transport.Show(isPlaying, isLooping, tempoBpm, lengthTicks, position);
            pianoRoll.SetPosition(positionTick);
        }
        /// <summary>保存状態と警告件数を表示する。</summary>
        public void ShowStatus(string text, int warningCount)
        {
            if (statusLabel.Text != text) { statusLabel.Text = text; }
            if (displayedWarningCount != warningCount)
            {
                warningsButton.Content = $"警告 {warningCount}";
                displayedWarningCount = warningCount;
            }
        }
        /// <summary>警告一覧を画面内へ展開する。</summary>
        public void ShowWarnings(string text)
        {
            warningsVisible = !warningsVisible;
            warningsText.Text = text;
            warningsText.IsVisible = warningsVisible;
        }
        /// <summary>タイマーとファイル通知を止めてから購読・所有物を破棄する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            watcher?.Dispose();
            displayTimer.Stop();
            displayTimer.Tick -= OnDisplayTick;
            tracks.TrackSelected -= OnTrackSelected;
            tracks.MuteRequested -= OnMuteRequested;
            transport.PlayRequested -= OnPlay;
            transport.StopRequested -= OnStop;
            transport.LoopRequested -= OnLoop;
            transport.TempoSubmitted -= OnTempo;
            transport.LengthSubmitted -= OnLength;
            pianoRoll.ZoomChanged -= OnZoom;
            rollScroll.ScrollChanged -= OnScroll;
            SizeChanged -= OnSizeChanged;
            warningsButton.Click -= OnWarnings;
            Opened -= OnOpened;
            Closed -= OnClosed;
            RemoveHandler(KeyDownEvent, OnShortcut);
            tracks.Dispose();
            instruments.Dispose();
            transport.Dispose();
            presenter?.Dispose();
        }
        private MainWindowPresenter MainPresenter => presenter ?? throw new InvalidOperationException("Program から Bind してください。");
        private TControl Require<TControl>(string name) where TControl : Control =>
            this.FindControl<TControl>(name) ?? throw new InvalidOperationException($"{name} がありません。");
        private void OnOpened(object? sender, EventArgs arguments)
        {
            int initialTopPitch = PianoRollPresenter.GetInitialTopPitch(MainPresenter.PianoRoll.Song);
            rollScroll.Offset = new Vector(0, (PianoRollPresenter.MaximumMidiNote - initialTopPitch) * PianoRollControl.NoteHeight);
            SynchronizeViewport();
            displayTimer.Start();
            pianoRoll.Focus();
        }
        private void OnClosed(object? sender, EventArgs arguments) => Dispose();
        private void OnDisplayTick(object? sender, EventArgs arguments) => MainPresenter.Execute(pollPlayback);
        private void OnTrackSelected(int trackIndex) => MainPresenter.Execute(() => MainPresenter.PianoRoll.SelectTrack(trackIndex));
        private void OnMuteRequested(int trackIndex) => MainPresenter.Execute(() => MainPresenter.ToggleMute(trackIndex));
        private void OnPlay() => MainPresenter.Execute(MainPresenter.Transport.TogglePlayback);
        private void OnStop() => MainPresenter.Execute(MainPresenter.Transport.Stop);
        private void OnLoop() => MainPresenter.Execute(MainPresenter.Transport.ToggleLoop);
        private void OnTempo(string text) => MainPresenter.Execute(() => MainPresenter.Transport.SetTempo(int.Parse(text, CultureInfo.InvariantCulture)));
        private void OnLength(string text) => MainPresenter.Execute(() => MainPresenter.Transport.SetLength(int.Parse(text, CultureInfo.InvariantCulture)));
        private void OnWarnings(object? sender, RoutedEventArgs arguments) => MainPresenter.Execute(MainPresenter.ShowWarnings);
        private void OnScroll(object? sender, ScrollChangedEventArgs arguments) => SynchronizeViewport();
        private void OnSizeChanged(object? sender, SizeChangedEventArgs arguments) => SynchronizeViewport();
        private void OnExternalChange()
        {
            if (!isDisposed) { MainPresenter.Execute(MainPresenter.ExternalFileChanged); }
        }
        private void OnZoom(double anchorTick, double previousScale)
        {
            double anchorOnScreen = anchorTick * previousScale - rollScroll.Offset.X;
            rollScroll.Offset = new Vector(Math.Max(0, anchorTick * pianoRoll.PixelsPerTick - anchorOnScreen), rollScroll.Offset.Y);
            SynchronizeViewport();
        }
        private void SynchronizeViewport()
        {
            if (song == null) { return; }
            pianoRoll.SetViewport(new Rect(rollScroll.Offset.X, rollScroll.Offset.Y, rollScroll.Viewport.Width, rollScroll.Viewport.Height));
            keyboard.SetOffset(rollScroll.Offset.Y);
            ruler.SetViewport(rollScroll.Offset.X, pianoRoll.PixelsPerTick, rollScroll.Viewport.Width, song.LengthTicks);
        }
        private void OnShortcut(object? sender, KeyEventArgs arguments)
        {
            bool control = arguments.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool shift = arguments.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool isParameterInput = FocusManager?.GetFocusedElement() is TextBox or ComboBox;
            Action? action = null;
            if (control && arguments.Key == Key.S) { action = MainPresenter.Save; }
            else if (isParameterInput) { return; }
            else if (control && arguments.Key == Key.Z) { action = shift ? MainPresenter.Redo : MainPresenter.Undo; }
            else if (!control) { action = GetPlainShortcut(arguments.Key); }
            if (action == null) { return; }
            MainPresenter.Execute(action);
            arguments.Handled = true;
        }
        private Action? GetPlainShortcut(Key key) => key switch
        {
            Key.Space => MainPresenter.Transport.TogglePlayback,
            Key.Delete => MainPresenter.PianoRoll.Delete,
            Key.Up => () => MainPresenter.PianoRoll.ChangeVolume(1),
            Key.Down => () => MainPresenter.PianoRoll.ChangeVolume(-1),
            Key.R => MainPresenter.ConfirmReload,
            _ => null
        };
    }
}
