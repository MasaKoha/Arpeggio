using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;
using Arpeggio.Daw.Audio.Sfx;
using Arpeggio.Daw.Editing.Sfx;
using Arpeggio.Daw.Platform.Sfx;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Presenters.Sfx;
using Arpeggio.Daw.Views.Sfx;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Arpeggio.Daw.Views
{
    /// <summary>固定の試聴・保存列と、スクロールするSFXパラメータ入力を接続する。</summary>
    public partial class SfxCreationView : UserControl, IDisposable
    {
        private const int SettingsQuietMilliseconds = 150;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private readonly Subject<Unit> saveRequests = new Subject<Unit>();
        private readonly SfxOutputPresenter output = new SfxOutputPresenter();
        private readonly ComboBox presets;
        private readonly ComboBox chips;
        private readonly ComboBox categories;
        private readonly ComboBox legacy;
        private readonly TextBox seed;
        private readonly TextBox strength;
        private readonly Slider monitor;
        private readonly CheckBox autoPreview;
        private readonly Button play;
        private MainWindowPresenter main = null!;
        private SfxParameterForm form = null!;
        private SfxParameterPanel parameters = null!;
        private SfxFileActions files = null!;
        private SfxUserSettings settings = new SfxUserSettings();
        private SfxEditResult? regeneration;
        private SfxSongCompilationResult? generation;
        private string? generationRevision;
        private string operationMessage = string.Empty;
        private string? displayedSourcePreset;
        private uint displayedSeed;
        private bool refreshing;
        private bool isDisposed;

        /// <summary>XAML の固定操作と選択欄を解決する。</summary>
        public SfxCreationView()
        {
            AvaloniaXamlLoader.Load(this);
            presets = Require<ComboBox>("PresetSelector");
            chips = Require<ComboBox>("ChipSelector");
            categories = Require<ComboBox>("CategorySelector");
            legacy = Require<ComboBox>("LegacySelector");
            seed = Require<TextBox>("SeedInput");
            strength = Require<TextBox>("StrengthInput");
            monitor = Require<Slider>("MonitorVolume");
            autoPreview = Require<CheckBox>("AutoPreviewToggle");
            play = Require<Button>("PlayButton");
        }

        private SfxEditorPresenter Editor => main.SfxEditor;
        private SfxPresetKind SelectedPreset => SfxPresetCatalog.GetAll()[Math.Max(0, presets.SelectedIndex)].Kind;
        private string SelectedCategory => categories.SelectedItem as string ?? "any";

        /// <summary>編集・試聴・保存と UI スレッドの入力時計を接続する。</summary>
        public void Bind(MainWindowPresenter mainPresenter)
        {
            main = mainPresenter;
            LoadSettings();
            var scheduler = new SynchronizationContextScheduler(new AvaloniaSynchronizationContext());
            form = new SfxParameterForm(Editor, scheduler);
            parameters = new SfxParameterPanel(Editor, form, settings);
            files = new SfxFileActions(this, Editor, output);
            Require<ContentControl>("ParameterHost").Content = parameters;
            InitializeChoices();
            BindActions();
            BindSelections();
            subscriptions.Add(Editor.Changes.Subscribe(_ => Refresh()));
            subscriptions.Add(main.SfxPreview.States.ObserveOn(scheduler).Subscribe(_ => Refresh()));
            subscriptions.Add(output.Changes.ObserveOn(scheduler).Subscribe(_ => Refresh()));
            subscriptions.Add(files.Changes.ObserveOn(scheduler).Subscribe(_ => Refresh()));
            subscriptions.Add(saveRequests.SelectMany(_ => Observable.FromAsync(SaveAsync)).Subscribe());
            subscriptions.Add(parameters.SettingsChanges.Merge(monitor.GetObservable(RangeBase.ValueProperty).Skip(1).Select(_ => Unit.Default))
                .Throttle(TimeSpan.FromMilliseconds(SettingsQuietMilliseconds), scheduler).Subscribe(_ => SaveSettings()));
            Refresh();
        }

        /// <summary>タブへ移ったときは再生へフォーカスを置く。</summary>
        public void EnterTab() => play.Focus();

        /// <summary>タブ離脱時は有効な途中値を一度確定し、試聴を止める。</summary>
        public void LeaveTab() => Editor.LeaveTab();

        /// <summary>文書の保存状態も含めて表示を更新する。</summary>
        public void Refresh()
        {
            if (isDisposed || refreshing) { return; }
            refreshing = true;
            try
            {
                Song song = Editor.Model.Snapshot();
                SfxSynchronizationState synchronization = Editor.Model.Synchronization;
                string revision = SfxHash.ComputeRevision(song);
                RefreshGeneration(song, synchronization, revision);
                parameters.Show(song, synchronization, generation);
                RefreshHeader(song, synchronization);
                RefreshActions(song, synchronization);
                RefreshDiagnostics();
                RefreshOutput(revision);
                Require<TextBlock>("OperationMessage").Text = string.Join("\n", new[] { Editor.Model.Error, operationMessage, files.Message, main.SfxPreview.State == SfxPreviewState.Failed ? main.SfxPreview.Failure?.Message ?? string.Empty : string.Empty }.Where(text => text.Length > 0));
                chips.SelectedItem = song.Chip;
                chips.IsEnabled = Editor.Model.IsNewCandidate;
                autoPreview.IsChecked = Editor.AutoPreview;
                string? sourcePreset = song.Sfx?.Known?.SourcePreset;
                if (sourcePreset != displayedSourcePreset)
                {
                    displayedSourcePreset = sourcePreset;
                    if (sourcePreset is not null) { presets.SelectedItem = sourcePreset; }
                }
                if (displayedSeed != Editor.Seed)
                {
                    displayedSeed = Editor.Seed;
                    seed.Text = displayedSeed.ToString(CultureInfo.InvariantCulture);
                }
                Require<TextBlock>("PresetDescription").Text = SfxParameterPresetCatalog.Get(SelectedPreset, song.Chip).Description;
            }
            finally { refreshing = false; }
        }

        /// <summary>文字編集を優先し、SFX 内のショートカットを通常曲から隔離する。</summary>
        public void HandleShortcut(KeyEventArgs arguments)
        {
            bool control = arguments.KeyModifiers.HasFlag(KeyModifiers.Control) || arguments.KeyModifiers.HasFlag(KeyModifiers.Meta);
            IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            bool textInput = focused is TextBox;
            if (arguments.Key == Key.Escape)
            {
                if (Editor.Model.HasGesture) { Editor.Cancel(); }
                else { Editor.Stop(); }
            }
            else if (control && arguments.Key == Key.S) { saveRequests.OnNext(Unit.Default); }
            else if (control && arguments.Key == Key.Z && !textInput)
            {
                if (arguments.KeyModifiers.HasFlag(KeyModifiers.Shift)) { Editor.Redo(); }
                else { Editor.Undo(); }
            }
            else if (arguments.Key == Key.Space && !textInput &&
                focused is not (ComboBox or ComboBoxItem or ToggleButton or NumericUpDown))
            {
                if (main.SfxPreview.State is SfxPreviewState.Playing or SfxPreviewState.Generating) { Editor.Stop(); }
                else { Editor.Play(); }
            }
            else { return; }
            arguments.Handled = true;
        }

        /// <summary>入力・ピッカー・結果の通知を止めて購読を解放する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            SaveSettings();
            isDisposed = true;
            subscriptions.Dispose();
            files.Dispose();
            parameters.Dispose();
            form.Dispose();
            output.Dispose();
            saveRequests.OnCompleted();
            saveRequests.Dispose();
        }

        private void InitializeChoices()
        {
            string[] names = SfxPresetCatalog.GetAll().Select(preset => preset.Name).ToArray();
            presets.ItemsSource = names;
            presets.SelectedIndex = 0;
            legacy.ItemsSource = names;
            legacy.SelectedIndex = 0;
            categories.ItemsSource = new[] { "any" }.Concat(names).ToArray();
            categories.SelectedIndex = 0;
            chips.ItemsSource = new[] { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes };
            chips.SelectedItem = Editor.Model.Chip;
            monitor.Value = settings.MonitorVolume;
            main.SfxPreview.Volume = settings.MonitorVolume;
            AutomationProperties.SetName(presets, "パラメータプリセット");
            AutomationProperties.SetName(legacy, "従来のソング雛形");
            AutomationProperties.SetName(chips, "新規候補のチップ");
            AutomationProperties.SetName(categories, "ランダムカテゴリ");
            AutomationProperties.SetName(seed, "乱数 seed、0から4294967295");
            AutomationProperties.SetName(strength, "変異の強さ、0から1");
            AutomationProperties.SetName(monitor, "試聴専用音量");
        }

        private void BindActions()
        {
            BindButton("PlayButton", Editor.Play);
            BindButton("StopButton", Editor.Stop);
            BindButton("NewCandidateButton", () => Editor.NewCandidate(Editor.Model.Chip, SelectedPreset));
            BindButton("FollowDocumentButton", Editor.FollowDocument);
            BindButton("RandomizeButton", () => CommitThen(() => Editor.Randomize(SelectedCategory)));
            BindButton("MutateButton", () => CommitThen(() => Editor.Mutate(ReadStrength())));
            BindButton("RepeatRandomButton", () => CommitThen(() => Editor.Randomize(SelectedCategory, ReadSeed())));
            BindButton("LegacyButton", () => Editor.NewLegacyCandidate(Editor.Model.Chip, SfxPresetCatalog.GetAll()[legacy.SelectedIndex].Kind));
            BindButton("UndoButton", Editor.Undo);
            BindButton("RedoButton", Editor.Redo);
            BindButton("SaveButton", () => saveRequests.OnNext(Unit.Default));
            BindButton("OpenSavedButton", Editor.OpenSaved);
            BindButton("DetachButton", Editor.Detach);
            BindButton("InspectRegenerationButton", InspectRegeneration);
            BindButton("ConfirmRegenerationButton", () =>
            {
                if (regeneration is null) { return; }
                Editor.Regenerate(regeneration.Revision);
                regeneration = null;
                Require<Button>("ConfirmRegenerationButton").IsEnabled = false;
            });
            subscriptions.Add(SfxViewEvents.Observe(Require<Button>("ExportWavButton"), Button.ClickEvent)
                .SelectMany(_ => Observable.FromAsync(files.ExportAsync)).Subscribe());
            subscriptions.Add(SfxViewEvents.Observe(Require<Button>("AnalyzeButton"), Button.ClickEvent)
                .SelectMany(_ => Observable.FromAsync(async () =>
                {
                    Editor.Commit();
                    if (Editor.Model.HasGesture) { return; }
                    Require<Expander>("OutputDetails").IsExpanded = true;
                    await output.AnalyzeAsync(Editor.Model.Snapshot());
                })).Subscribe());
        }

        private void BindSelections()
        {
            subscriptions.Add(presets.GetObservable(SelectingItemsControl.SelectedIndexProperty).Skip(1)
                .Where(index => !refreshing && index >= 0).Subscribe(_ => CommitThen(() =>
                {
                    if (Editor.Model.Synchronization.Editable) { Editor.SelectPreset(SelectedPreset); }
                    else { Editor.NewCandidate(Editor.Model.Chip, SelectedPreset); }
                })));
            subscriptions.Add(chips.GetObservable(SelectingItemsControl.SelectedItemProperty).Skip(1)
                .Where(chip => !refreshing && chip is ChipKind).Subscribe(chip => Editor.NewCandidate((ChipKind)chip!, SelectedPreset)));
            subscriptions.Add(autoPreview.GetObservable(ToggleButton.IsCheckedProperty).Skip(1)
                .Where(_ => !refreshing).Subscribe(value => Editor.AutoPreview = value == true));
            subscriptions.Add(monitor.GetObservable(RangeBase.ValueProperty).Subscribe(value =>
            {
                main.SfxPreview.Volume = (float)value;
                settings.MonitorVolume = (float)value;
                Require<TextBlock>("MonitorLabel").Text = FormattableString.Invariant($"試聴音量 {value:P0}");
            }));
        }

        private void RefreshHeader(Song song, SfxSynchronizationState synchronization)
        {
            string source = Editor.Model.IsNewCandidate ? "新規候補" : Path.GetFileName(main.DocumentPath);
            Require<TextBlock>("HeaderText").Text = $"SFX · {song.Chip} · {source}";
            string state = Editor.Model.State switch
            {
                SfxEditingState.Dragging => "操作中（未確定）",
                SfxEditingState.Invalid => "無効入力 · 最後の有効値を保持",
                SfxEditingState.SaveFailed => "保存失敗 · 再試行できます",
                SfxEditingState.Conflict => "外部競合 · 保存／再読み込みが必要",
                SfxEditingState.Disposed => "破棄中",
                _ => synchronization.Reason switch
                {
                    SfxEditabilityReason.MissingDefinition => "SFX 定義なし · 通常ソング／従来雛形",
                    SfxEditabilityReason.GeneratedContentChanged => "生成列を編集済み",
                    SfxEditabilityReason.SavedParametersChanged => "保存パラメータを手修正済み",
                    SfxEditabilityReason.UnsupportedSfxVersion => "未知版 · パラメータ編集不可",
                    _ => "同期済み"
                }
            };
            string preview = main.SfxPreview.State switch
            {
                SfxPreviewState.Generating => "生成中…",
                SfxPreviewState.Playing => "試聴中",
                SfxPreviewState.Failed => "試聴失敗 · 詳細を確認",
                SfxPreviewState.Disposed => "破棄中",
                _ => "停止中"
            };
            Require<TextBlock>("StateText").Text = state + " · " + preview;
            play.Content = Editor.PlayLabel;
        }

        private void RefreshActions(Song song, SfxSynchronizationState synchronization)
        {
            bool candidate = Editor.Model.IsNewCandidate;
            bool busy = files.IsPicking || output.IsRunning;
            Require<Button>("OpenSavedButton").IsVisible = candidate;
            Require<Button>("OpenSavedButton").IsEnabled = Editor.CanOpen && !busy;
            Require<Button>("SaveButton").Content = candidate ? "新規保存" : "保存（Ctrl+S）";
            Require<Button>("SaveButton").IsEnabled = !busy;
            Require<Button>("DetachButton").IsVisible = !candidate && song.Sfx is not null;
            Require<Button>("UndoButton").IsEnabled = Editor.Model.UndoCount > 0;
            Require<Button>("RedoButton").IsEnabled = Editor.Model.RedoCount > 0;
            Require<Button>("RandomizeButton").IsEnabled = synchronization.Editable;
            Require<Button>("MutateButton").IsEnabled = synchronization.Editable;
            Require<Button>("RepeatRandomButton").IsEnabled = synchronization.Editable;
            Require<Button>("AnalyzeButton").IsEnabled = !busy;
            Require<Button>("ExportWavButton").IsEnabled = !busy;
            Require<StackPanel>("RegenerationPanel").IsVisible = !candidate && synchronization.Reason is
                SfxEditabilityReason.GeneratedContentChanged or SfxEditabilityReason.SavedParametersChanged;
            string saveState = main.IsDirty ? "未保存" : "保存済み";
            if (candidate)
            {
                saveState = Editor.CandidateFile.SavedPath is null ? "未保存の新規候補" :
                    Editor.CanOpen ? "候補保存済み" : "保存したSFXは古い値です。最新候補を新規保存してください。";
            }
            Require<TextBlock>("SaveStateText").Text = saveState;
        }

        private void RefreshGeneration(Song song, SfxSynchronizationState synchronization, string revision)
        {
            if (revision == generationRevision) { return; }
            generationRevision = revision;
            generation = synchronization.Editable ? SfxSongCompiler.Compile(synchronization.Parameters!, song.Chip) : null;
            regeneration = null;
            Require<Button>("ConfirmRegenerationButton").IsEnabled = false;
            Require<TextBlock>("ReplacementText").Text = string.Empty;
        }

        private void RefreshDiagnostics()
        {
            Require<TextBlock>("DiagnosticsText").Text = generation is null ? "保存済み生成列をそのまま再生します。" :
                FormattableString.Invariant($"本体 {generation.Curves.BodyDurationSeconds:0.######} 秒（終端保持込み）· 診断 {generation.Warnings.Count} 件");
            Require<TextBlock>("DiagnosticsDetail").Text = generation is null ? string.Empty : string.Join("\n", generation.Warnings.Select(warning =>
                FormattableString.Invariant($"{warning.Code} · {warning.ParameterPath} · frame {warning.FromFrame}〜{warning.ToFrame}: {warning.Requested:0.######} → {warning.Actual:0.######}\n{warning.Message}")));
        }

        private void RefreshOutput(string revision)
        {
            Require<TextBlock>("OutputStatus").Text = output.Revision is null ? string.Empty :
                (output.Revision == revision ? "現在候補の結果" : "古い結果 · 編集後の候補は未解析／未出力") +
                (output.IsRunning ? " · 処理中…" : string.Empty);
            Require<TextBox>("OutputText").Text = output.Revision is null ? string.Empty : "revision: " + output.Revision + "\n" + output.Result;
        }

        private void InspectRegeneration()
        {
            regeneration = Editor.InspectRegeneration();
            SfxReplacementSummary replacement = regeneration.Replacement!;
            Require<TextBlock>("ReplacementText").Text = $"音色 {replacement.InstrumentCount}・トラック {replacement.TrackCount}・ノート {replacement.NoteCount} を全置換します。元に戻す一回で復元できます。";
            Require<Button>("ConfirmRegenerationButton").IsEnabled = true;
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            if (Editor.Model.IsNewCandidate) { await files.SaveAsync(); }
            else { main.Execute(main.Save); Refresh(); }
        }

        private void CommitThen(Action action)
        {
            Editor.Commit();
            if (!Editor.Model.HasGesture) { action(); }
        }

        private uint ReadSeed() => uint.TryParse(seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint value)
            ? value : throw new ArgumentException("seed は0〜4294967295の整数です。");

        private double ReadStrength()
        {
            if (!double.TryParse(strength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0 || value > 1)
            {
                throw new ArgumentException("変異の強さは0〜1です。");
            }
            return value;
        }

        private void BindButton(string name, Action action) => subscriptions.Add(
            SfxViewEvents.Observe(Require<Button>(name), Button.ClickEvent).Subscribe(_ =>
            {
                try { operationMessage = string.Empty; action(); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    operationMessage = exception.Message;
                }
                Refresh();
            }));

        private void LoadSettings()
        {
            try { settings = SfxUserSettings.Load(SfxUserSettings.DefaultPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                operationMessage = "設定を読み込めません。既定値を使います: " + exception.Message;
            }
        }

        private void SaveSettings()
        {
            try { settings.Save(SfxUserSettings.DefaultPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                operationMessage = "設定を保存できません: " + exception.Message;
                Refresh();
            }
        }

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
    }
}
