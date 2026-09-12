using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Arpeggio.Formats.Midi;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Daw.Views
{
    /// <summary>MIDI の設定・ファイル選択・診断・保存と別操作 Open を非モーダルに接続する。</summary>
    public partial class MidiImportView : UserControl, IDisposable
    {
        private readonly StackPanel settingsPanel;
        private readonly TextBox sourceInput;
        private readonly TextBox destinationInput;
        private readonly TextBox tempoInput;
        private readonly TextBox quantizeInput;
        private readonly TextBox mapInput;
        private readonly TextBox titleInput;
        private readonly ComboBox chipInput;
        private readonly ComboBox polyphonyInput;
        private readonly CheckBox strictInput;
        private readonly Button sourceButton;
        private readonly Button destinationButton;
        private readonly Button mapButton;
        private readonly Button prepareButton;
        private readonly Button saveButton;
        private readonly Button openButton;
        private readonly Button cancelButton;
        private readonly TextBlock statusLabel;
        private readonly TextBox reportText;
        private MidiImportPresenter importPresenter = null!;
        private Window window = null!;
        private string? pickerError;
        private bool isPicking;
        private bool isDisposed;

        /// <summary>入力部品を取得し、対応チップと声数不足時の候補を用意する。</summary>
        public MidiImportView()
        {
            AvaloniaXamlLoader.Load(this);
            settingsPanel = Require<StackPanel>("SettingsPanel");
            sourceInput = Require<TextBox>("SourceInput");
            destinationInput = Require<TextBox>("DestinationInput");
            tempoInput = Require<TextBox>("TempoInput");
            quantizeInput = Require<TextBox>("QuantizeInput");
            mapInput = Require<TextBox>("MapInput");
            titleInput = Require<TextBox>("TitleInput");
            chipInput = Require<ComboBox>("ChipInput");
            polyphonyInput = Require<ComboBox>("PolyphonyInput");
            strictInput = Require<CheckBox>("StrictInput");
            sourceButton = Require<Button>("SourceButton");
            destinationButton = Require<Button>("DestinationButton");
            mapButton = Require<Button>("MapButton");
            prepareButton = Require<Button>("PrepareButton");
            saveButton = Require<Button>("SaveButton");
            openButton = Require<Button>("OpenButton");
            cancelButton = Require<Button>("CancelButton");
            statusLabel = Require<TextBlock>("StatusLabel");
            reportText = Require<TextBox>("ReportText");
            chipInput.ItemsSource = new[] { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes };
            chipInput.SelectedItem = ChipKind.Nes;
            polyphonyInput.ItemsSource = new[] { MidiPolyphonyMode.StealOldest, MidiPolyphonyMode.DropNew };
            polyphonyInput.SelectedItem = MidiPolyphonyMode.StealOldest;
        }

        /// <summary>Presenter と OS ピッカーの所有ウィンドウを接続する。</summary>
        public void Bind(MidiImportPresenter presenter, Window owner)
        {
            importPresenter = presenter;
            window = owner;
            foreach (TextBox input in TextInputs()) { input.TextChanged += OnTextChanged; }
            chipInput.SelectionChanged += OnSelectionChanged;
            polyphonyInput.SelectionChanged += OnSelectionChanged;
            strictInput.IsCheckedChanged += OnStrictChanged;
            sourceButton.Click += OnSource;
            destinationButton.Click += OnDestination;
            mapButton.Click += OnMap;
            prepareButton.Click += OnPrepare;
            saveButton.Click += OnSave;
            openButton.Click += OnOpen;
            cancelButton.Click += OnCancel;
            Refresh();
        }

        /// <summary>実行状態と候補に基づいて入力・診断・操作可否を表示する。</summary>
        public void Refresh()
        {
            if (isDisposed) { return; }
            bool available = !isPicking && !importPresenter.IsRunning;
            settingsPanel.IsEnabled = available;
            prepareButton.IsEnabled = available;
            saveButton.IsEnabled = !isPicking && importPresenter.CanSave;
            openButton.IsEnabled = !isPicking && importPresenter.CanOpen;
            cancelButton.IsEnabled = !isPicking;
            statusLabel.Text = pickerError ?? importPresenter.StatusText;
            reportText.Text = importPresenter.ReportText;
        }

        /// <summary>入力購読を解放し、終了後のピッカー結果を破棄する。</summary>
        public void Dispose()
        {
            isDisposed = true;
            foreach (TextBox input in TextInputs()) { input.TextChanged -= OnTextChanged; }
            chipInput.SelectionChanged -= OnSelectionChanged;
            polyphonyInput.SelectionChanged -= OnSelectionChanged;
            strictInput.IsCheckedChanged -= OnStrictChanged;
            sourceButton.Click -= OnSource;
            destinationButton.Click -= OnDestination;
            mapButton.Click -= OnMap;
            prepareButton.Click -= OnPrepare;
            saveButton.Click -= OnSave;
            openButton.Click -= OnOpen;
            cancelButton.Click -= OnCancel;
        }

        private TextBox[] TextInputs() => new[] { sourceInput, destinationInput, tempoInput, quantizeInput, mapInput, titleInput };
        private void OnTextChanged(object? sender, TextChangedEventArgs arguments) => InvalidateSettings();
        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs arguments) => InvalidateSettings();
        private void OnStrictChanged(object? sender, RoutedEventArgs arguments) => InvalidateSettings();
        private void OnOpen(object? sender, RoutedEventArgs arguments)
        {
            pickerError = null;
            importPresenter.OpenSaved();
        }
        private void OnCancel(object? sender, RoutedEventArgs arguments)
        {
            pickerError = null;
            importPresenter.Cancel();
        }
        private async void OnSave(object? sender, RoutedEventArgs arguments)
        {
            pickerError = null;
            await importPresenter.SaveAsync();
        }
        private async void OnSource(object? sender, RoutedEventArgs arguments) => await PickAsync(sourceInput, false);
        private async void OnMap(object? sender, RoutedEventArgs arguments) => await PickAsync(mapInput, false);
        private async void OnDestination(object? sender, RoutedEventArgs arguments) => await PickAsync(destinationInput, true);

        private void InvalidateSettings()
        {
            pickerError = null;
            importPresenter.InvalidateCandidate();
        }

        private async void OnPrepare(object? sender, RoutedEventArgs arguments)
        {
            pickerError = null;
            await importPresenter.PrepareAsync(new MidiImportInput
            {
                SourcePath = sourceInput.Text ?? string.Empty, DestinationPath = destinationInput.Text ?? string.Empty,
                Chip = chipInput.SelectedItem is ChipKind chip ? chip : ChipKind.None,
                Tempo = tempoInput.Text ?? string.Empty, QuantizeTicks = quantizeInput.Text ?? string.Empty,
                Polyphony = polyphonyInput.SelectedItem is MidiPolyphonyMode mode ? mode : MidiPolyphonyMode.None,
                ChannelMapPath = mapInput.Text ?? string.Empty, Title = titleInput.Text ?? string.Empty,
                Strict = strictInput.IsChecked == true
            });
        }

        private async Task PickAsync(TextBox target, bool save)
        {
            if (isDisposed || isPicking || importPresenter.IsRunning) { return; }
            isPicking = true;
            pickerError = null;
            Refresh();
            try
            {
                string? path = save ? await PickDestinationAsync() : await PickSourceAsync(target == sourceInput);
                if (!isDisposed && path != null) { target.Text = path; }
            }
            catch (Exception exception)
            {
                if (!isDisposed) { pickerError = $"ファイル選択に失敗しました: {exception.Message}"; }
            }
            finally
            {
                isPicking = false;
                if (!isDisposed) { Refresh(); }
            }
        }

        private async Task<string?> PickDestinationAsync()
        {
            using IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "MIDI の新規保存先（既存ファイルは上書きしません）", SuggestedFileName = "imported.arpeggio.json",
                DefaultExtension = "json", ShowOverwritePrompt = false,
                FileTypeChoices = new[] { new FilePickerFileType("Arpeggio JSON") { Patterns = new[] { "*.arpeggio.json" } } }
            });
            return file is null ? null : RequireLocalPath(file);
        }

        private async Task<string?> PickSourceAsync(bool midi)
        {
            var type = new FilePickerFileType(midi ? "MIDI SMF" : "channel-map JSON")
            {
                Patterns = midi ? new[] { "*.mid", "*.midi" } : new[] { "*.json" }
            };
            IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = midi ? "MIDI を選択" : "channel-map を選択", AllowMultiple = false, FileTypeFilter = new[] { type }
            });
            try { return files.Count == 0 ? null : RequireLocalPath(files[0]); }
            finally { foreach (IStorageFile file in files) { file.Dispose(); } }
        }

        private static string RequireLocalPath(IStorageFile file) => file.TryGetLocalPath()
            ?? throw new InvalidOperationException("ローカルファイルを選択してください。");

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
    }
}
