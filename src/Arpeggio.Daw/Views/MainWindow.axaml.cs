using System;
using System.Globalization;
using System.Threading.Tasks;
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
        private const int SfxTabIndex = 2;
        private readonly PianoRollControl pianoRoll;
        private readonly KeyboardStripControl keyboard;
        private readonly TimeRulerControl ruler;
        private readonly ScrollViewer rollScroll;
        private readonly TrackListView tracks;
        private readonly InstrumentPanelView instruments;
        private readonly TransportView transport;
        private readonly NotePanelView notes;
        private readonly AnalysisView analysis;
        private readonly SfxCreationView sfxCreation;
        private readonly TabControl editorTabs;
        private readonly Button sfxButton;
        private readonly Button exportButton;
        private readonly Button revealExportButton;
        private readonly AudioFilePicker filePicker;
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
            notes = Require<NotePanelView>("Notes");
            analysis = Require<AnalysisView>("Analysis");
            sfxCreation = Require<SfxCreationView>("SfxCreation");
            editorTabs = Require<TabControl>("EditorTabs");
            sfxButton = Require<Button>("SfxButton");
            exportButton = Require<Button>("ExportButton");
            revealExportButton = Require<Button>("RevealExportButton");
            filePicker = new AudioFilePicker(this);
        }
        /// <summary>Program が組み立てた Presenter と明示的に結線する。</summary>
        public void Bind(MainWindowPresenter mainPresenter, string path)
        {
            presenter = mainPresenter;
            pollPlayback = mainPresenter.Poll;
            pianoRoll.Bind(mainPresenter.PianoRoll, mainPresenter.Execute);
            instruments.Bind(mainPresenter.Instruments, mainPresenter.Execute);
            notes.Bind(mainPresenter.Notes, mainPresenter.Execute);
            analysis.Bind(mainPresenter.Analysis);
            sfxCreation.Bind(mainPresenter.SfxCreation, mainPresenter.Execute);
            instruments.WavImportRequested += OnImportWav;
            sfxButton.Click += OnSfx;
            exportButton.Click += OnExport;
            revealExportButton.Click += OnRevealExport;
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
            instruments.ShowChannel(current.Tracks[selectedTrack]);
            instruments.Refresh();
            notes.Refresh();
            analysis.ShowTracks(current);
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
                warningsButton.Classes.Set("danger", warningCount > 0);
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
        /// <summary>解析パネルへ結果と実行状態を渡す。</summary>
        public void ShowAnalysis(string text, bool isRunning) => analysis.ShowAnalysis(text, isRunning);
        /// <summary>書き出し操作の二重起動を抑止する。</summary>
        public void ShowExportStatus(string text, bool isRunning, string? lastExportedPath)
        {
            exportButton.Content = isRunning ? "書き出し中…" : "書き出し";
            exportButton.IsEnabled = !isRunning;
            revealExportButton.IsVisible = lastExportedPath != null;
            revealExportButton.IsEnabled = !isRunning;
        }
        /// <summary>旧ファイルの監視を解放して新しい正本へ切り替える。</summary>
        public void SwitchDocument(string path)
        {
            watcher?.Dispose();
            watcher = new SongFileWatcher(path, () => Dispatcher.UIThread.Post(OnExternalChange));
            sfxCreation.ResetDestination();
            editorTabs.SelectedIndex = 0;
            ResetViewport();
        }
        /// <summary>バックグラウンドの完了通知を UI スレッドへ戻す。</summary>
        public async Task RunOnUiThreadAsync(Action action)
        {
            if (isDisposed) { return; }
            await Dispatcher.UIThread.InvokeAsync(action);
        }
        /// <summary>タイマーとファイル通知を止めてから購読・所有物を破棄する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            filePicker.Dispose();
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
            instruments.WavImportRequested -= OnImportWav;
            sfxButton.Click -= OnSfx;
            exportButton.Click -= OnExport;
            revealExportButton.Click -= OnRevealExport;
            Opened -= OnOpened;
            Closed -= OnClosed;
            RemoveHandler(KeyDownEvent, OnShortcut);
            tracks.Dispose();
            instruments.Dispose();
            notes.Dispose();
            analysis.Dispose();
            sfxCreation.Dispose();
            transport.Dispose();
            presenter?.Dispose();
        }
        private MainWindowPresenter MainPresenter => presenter ?? throw new InvalidOperationException("Program から Bind してください。");
        private TControl Require<TControl>(string name) where TControl : Control =>
            this.FindControl<TControl>(name) ?? throw new InvalidOperationException($"{name} がありません。");
        private void OnOpened(object? sender, EventArgs arguments)
        {
            ResetViewport();
            displayTimer.Start();
            pianoRoll.Focus();
        }
        private void ResetViewport()
        {
            int initialTopPitch = PianoRollPresenter.GetInitialTopPitch(MainPresenter.PianoRoll.Song);
            rollScroll.Offset = new Vector(0, (PianoRollPresenter.MaximumMidiNote - initialTopPitch) * PianoRollControl.NoteHeight);
            SynchronizeViewport();
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
        private void OnSfx(object? sender, RoutedEventArgs arguments) => editorTabs.SelectedIndex = SfxTabIndex;
        private async void OnExport(object? sender, RoutedEventArgs arguments) => await filePicker.ExportAsync(MainPresenter);
        private void OnRevealExport(object? sender, RoutedEventArgs arguments) => MainPresenter.Execute(MainPresenter.Export.RevealLastExport);
        private async void OnImportWav(string rootNote, bool loop) => await filePicker.ImportAsync(MainPresenter, rootNote, loop);
        private async void ExportWithPicker() => await filePicker.ExportAsync(MainPresenter);
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
            bool isParameterInput = FocusManager?.GetFocusedElement() is TextBox or ComboBox or ListBox or ListBoxItem or Button;
            Action? action = null;
            if (control && arguments.Key == Key.S) { action = MainPresenter.Save; }
            else if (control && arguments.Key == Key.E) { action = ExportWithPicker; }
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
