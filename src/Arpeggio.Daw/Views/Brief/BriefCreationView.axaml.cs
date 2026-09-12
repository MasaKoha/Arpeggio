using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters.Brief;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Arpeggio.Daw.Views.Brief
{
    /// <summary>作曲指示書のフォーム入力と OS のファイル選択・クリップボードを接続する。</summary>
    public partial class BriefCreationView : UserControl, IDisposable
    {
        private const string BriefExtension = "brief.json";
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private readonly Subject<Unit> saveRequests = new Subject<Unit>();
        private readonly ChipKind?[] chipChoices = { null, ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes };
        private readonly ComboBox chips;
        private readonly StackPanel formFields;
        private readonly Button saveButton;
        private readonly Button openButton;
        private readonly Button copyButton;
        private BriefEditorPresenter editor = null!;
        private bool isBound;
        private bool isRefreshing;
        private bool isBusy;
        private bool isDisposed;

        /// <summary>XAML の入力・操作部品を解決する。</summary>
        public BriefCreationView()
        {
            AvaloniaXamlLoader.Load(this);
            chips = Require<ComboBox>("ChipSelector");
            formFields = Require<StackPanel>("FormFields");
            saveButton = Require<Button>("SaveButton");
            openButton = Require<Button>("OpenButton");
            copyButton = Require<Button>("CopyButton");
        }

        /// <summary>MainWindow が所有する Presenter をフォームへ接続する。</summary>
        public void Bind(BriefEditorPresenter briefEditor)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (isBound) { throw new InvalidOperationException("作曲指示書は接続済みです。"); }
            editor = briefEditor;
            isBound = true;
            chips.ItemsSource = new[] { "指定なし", "NES", "Game Boy", "SNES" };
            ShowBrief();
            SetEvent();
            RefreshStatus();
        }

        /// <summary>指示書の保存キーだけを処理し、文字編集キーはフォームへ渡す。</summary>
        public void HandleShortcut(KeyEventArgs arguments)
        {
            bool control = arguments.KeyModifiers.HasFlag(KeyModifiers.Control) || arguments.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (control && arguments.Key == Key.S)
            {
                saveRequests.OnNext(Unit.Default);
                arguments.Handled = true;
            }
        }

        /// <summary>入力・一時表示・操作購読を解放し、ピッカーの遅延完了を無効にする。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            subscriptions.Dispose();
            saveRequests.OnCompleted();
            saveRequests.Dispose();
        }

        private void SetEvent()
        {
            var scheduler = new SynchronizationContextScheduler(new AvaloniaSynchronizationContext());
            BindTextInput("TitleInput", editor.UpdateTitle);
            BindTextInput("TempoInput", editor.UpdateTempo);
            BindTextInput("MoodInput", editor.UpdateMood);
            BindTextInput("StructureInput", editor.UpdateStructure);
            BindTextInput("InstrumentationInput", editor.UpdateInstrumentation);
            BindTextInput("ReferencesInput", editor.UpdateReferences);
            BindTextInput("ConstraintsInput", editor.UpdateConstraints);
            BindTextInput("NotesInput", editor.UpdateNotes);
            subscriptions.Add(chips.GetObservable(SelectingItemsControl.SelectedIndexProperty).Skip(1)
                .Where(index => !isRefreshing && index >= 0 && index < chipChoices.Length)
                .Subscribe(index => editor.UpdateChip(chipChoices[index])));
            subscriptions.Add(editor.Changes.Subscribe(_ => RefreshStatus()));
            subscriptions.Add(editor.ObserveMessages(scheduler).Subscribe(message => Require<TextBlock>("OperationMessage").Text = message));
            subscriptions.Add(ObserveClicks(saveButton).Merge(saveRequests)
                .SelectMany(_ => Observable.FromAsync(() => RunOperationAsync(SaveAsync))).Subscribe());
            subscriptions.Add(ObserveClicks(openButton)
                .SelectMany(_ => Observable.FromAsync(() => RunOperationAsync(OpenAsync))).Subscribe());
            subscriptions.Add(ObserveClicks(copyButton)
                .SelectMany(_ => Observable.FromAsync(() => RunOperationAsync(CopyAsync))).Subscribe());
        }

        private void BindTextInput(string name, Action<string?> update) => subscriptions.Add(
            Require<TextBox>(name).GetObservable(TextBox.TextProperty).Skip(1)
                .Where(_ => !isRefreshing).Subscribe(update));

        private static IObservable<Unit> ObserveClicks(Button button) => Observable.Create<Unit>(observer =>
            button.AddDisposableHandler(Button.ClickEvent, (_, _) => observer.OnNext(Unit.Default)));

        private void ShowBrief()
        {
            isRefreshing = true;
            try
            {
                CompositionBrief brief = editor.Brief;
                Require<TextBox>("TitleInput").Text = brief.Title;
                chips.SelectedIndex = Array.IndexOf(chipChoices, brief.Chip);
                Require<TextBox>("TempoInput").Text = brief.TempoBpm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                Require<TextBox>("MoodInput").Text = brief.Mood ?? string.Empty;
                Require<TextBox>("StructureInput").Text = brief.Structure ?? string.Empty;
                Require<TextBox>("InstrumentationInput").Text = brief.Instrumentation ?? string.Empty;
                Require<TextBox>("ReferencesInput").Text = brief.References ?? string.Empty;
                Require<TextBox>("ConstraintsInput").Text = brief.Constraints ?? string.Empty;
                Require<TextBox>("NotesInput").Text = brief.Notes ?? string.Empty;
            }
            finally { isRefreshing = false; }
        }

        private void RefreshStatus()
        {
            Require<TextBlock>("ErrorText").Text = editor.Error;
            Require<TextBlock>("DocumentPathText").Text = editor.DocumentPath ?? "未保存の作曲指示書";
        }

        private async Task RunOperationAsync(Func<Task> operation)
        {
            if (isDisposed || isBusy) { return; }
            SetBusy(true);
            try { await operation(); }
            catch (Exception exception)
            {
                editor.ReportFailure("操作に失敗しました", exception);
            }
            finally
            {
                if (!isDisposed) { SetBusy(false); }
            }
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            formFields.IsEnabled = !busy;
            saveButton.IsEnabled = !busy;
            openButton.IsEnabled = !busy;
            copyButton.IsEnabled = !busy;
        }

        private async Task SaveAsync()
        {
            if (editor.DocumentPath is string savedPath)
            {
                editor.Save(savedPath);
                return;
            }
            TopLevel window = RequireWindow();
            using IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "作曲指示書を保存", SuggestedFileName = "composition." + BriefExtension,
                DefaultExtension = BriefExtension, FileTypeChoices = new[] { BriefFileType() }, ShowOverwritePrompt = true
            });
            if (isDisposed || file is null) { return; }
            editor.Save(RequireLocalPath(file));
        }

        private async Task OpenAsync()
        {
            IReadOnlyList<IStorageFile> files = await RequireWindow().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "作曲指示書を開く", AllowMultiple = false, FileTypeFilter = new[] { BriefFileType() }
            });
            try
            {
                if (isDisposed || files.Count == 0) { return; }
                if (editor.Open(RequireLocalPath(files[0]))) { ShowBrief(); }
            }
            finally
            {
                foreach (IStorageFile file in files) { file.Dispose(); }
            }
        }

        private async Task CopyAsync() => await editor.CopyTextAsync(WriteClipboardTextAsync);

        private async Task WriteClipboardTextAsync(string text)
        {
            IClipboard clipboard = RequireWindow().Clipboard ?? throw new InvalidOperationException("クリップボードを利用できません。");
            await clipboard.SetTextAsync(text);
        }

        private TopLevel RequireWindow() => TopLevel.GetTopLevel(this)
            ?? throw new InvalidOperationException("作曲指示書の画面がありません。");

        private static FilePickerFileType BriefFileType() => new FilePickerFileType("Arpeggio 作曲指示書")
        {
            Patterns = new[] { "*." + BriefExtension }
        };

        private static string RequireLocalPath(IStorageFile file) => file.TryGetLocalPath()
            ?? throw new IOException("ローカルファイルを指定してください。");

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
    }
}
