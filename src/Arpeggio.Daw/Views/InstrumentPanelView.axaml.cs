using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Themes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw.Views
{
    /// <summary>音色項目を表示し、確定した入力だけを Presenter へ渡す。</summary>
    public partial class InstrumentPanelView : UserControl, IDisposable
    {
        private const int FieldSpacing = 4;
        private readonly ComboBox instrumentSelector;
        private readonly TextBlock kindLabel;
        private readonly TextBox nameInput;
        private readonly StackPanel parameterPanel;
        private readonly Button addButton;
        private readonly Button removeButton;
        private readonly Button applyButton;
        private readonly StackPanel sampleImportPanel;
        private readonly TextBlock sampleSummary;
        private readonly TextBox rootNoteInput;
        private readonly CheckBox loopSampleInput;
        private readonly Button importWavButton;
        private readonly Button clearSampleButton;
        private readonly StackPanel presetPanel;
        private readonly ComboBox presetSelector;
        private readonly SnesDspView snesDsp;
        private readonly List<ComboBoxItem> presetItems = new List<ComboBoxItem>();
        private readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>();
        private InstrumentPanelPresenter presenter = null!;
        private Action<Action> execute = null!;
        private IReadOnlyList<Instrument> instruments = Array.Empty<Instrument>();
        private IReadOnlyList<Instrument>? displayedInstrumentSnapshot;
        private Instrument? displayedInstrument;
        private InstrumentKind displayedKind;
        private bool isRefreshing;
        private bool isBound;
        private string channelLabel = string.Empty;

        /// <summary>XAML の表示部品を解決する。</summary>
        public InstrumentPanelView()
        {
            AvaloniaXamlLoader.Load(this);
            instrumentSelector = RequireControl<ComboBox>("InstrumentSelector");
            kindLabel = RequireControl<TextBlock>("KindLabel");
            nameInput = RequireControl<TextBox>("NameInput");
            parameterPanel = RequireControl<StackPanel>("ParameterPanel");
            addButton = RequireControl<Button>("AddButton");
            removeButton = RequireControl<Button>("RemoveButton");
            applyButton = RequireControl<Button>("ApplyButton");
            sampleImportPanel = RequireControl<StackPanel>("SampleImportPanel");
            sampleSummary = RequireControl<TextBlock>("SampleSummary");
            rootNoteInput = RequireControl<TextBox>("RootNoteInput");
            loopSampleInput = RequireControl<CheckBox>("LoopSampleInput");
            importWavButton = RequireControl<Button>("ImportWavButton");
            clearSampleButton = RequireControl<Button>("ClearSampleButton");
            presetPanel = RequireControl<StackPanel>("PresetPanel");
            presetSelector = RequireControl<ComboBox>("PresetSelector");
            snesDsp = RequireControl<SnesDspView>("SnesDsp");
        }

        /// <summary>ルート音名とループ指定を添えて OS ファイル選択を要求する。</summary>
        public event Action<string, bool>? WavImportRequested;

        /// <summary>Program で組み立てた Presenter と例外表示境界を接続する。</summary>
        public void Bind(InstrumentPanelPresenter presenter, Action<Action> execute)
        {
            if (isBound)
            {
                throw new InvalidOperationException("音色パネルは接続済みです。");
            }
            this.presenter = presenter;
            this.execute = execute;
            isBound = true;
            PopulatePresets();
            presetSelector.SelectionChanged += OnPresetSelected;
            clearSampleButton.Click += OnClearSample;
            snesDsp.NoiseChanged += UpdateWaveformAvailability;
            instrumentSelector.SelectionChanged += OnInstrumentSelected;
            addButton.Click += OnAddClicked;
            removeButton.Click += OnRemoveClicked;
            applyButton.Click += OnApplyClicked;
            importWavButton.Click += OnImportWav;
            Refresh();
        }

        /// <summary>選択音色の種類に対応する入力だけを表示する。</summary>
        public void Refresh()
        {
            if (!isBound)
            {
                return;
            }
            IReadOnlyList<Instrument> snapshot = presenter.InstrumentSnapshot;
            Instrument? current = presenter.CurrentInstrument;
            InstrumentKind kind = presenter.RequiredKind;
            snesDsp.ShowPitchModulationAvailability(presenter.CanUsePitchModulation);
            if (ReferenceEquals(snapshot, displayedInstrumentSnapshot) &&
                ReferenceEquals(current, displayedInstrument) && kind == displayedKind)
            {
                return;
            }
            isRefreshing = true;
            try
            {
                instruments = presenter.Instruments;
                UpdateKindLabel(kind);
                instrumentSelector.ItemsSource = instruments.Select(instrument => $"{instrument.Id}: {instrument.Name}").ToArray();
                instrumentSelector.SelectedIndex = FindInstrumentIndex(current?.Id);
                nameInput.Text = current?.Name ?? string.Empty;
                nameInput.IsEnabled = current != null;
                removeButton.IsEnabled = current != null;
                applyButton.IsEnabled = current != null;
                sampleImportPanel.IsVisible = kind == InstrumentKind.SnesSample;
                importWavButton.IsEnabled = current is SnesSampleInstrument;
                sampleSummary.Text = current is SnesSampleInstrument sample ? sample.SampleSummary : string.Empty;
                presetPanel.IsVisible = kind == InstrumentKind.SnesSample;
                presetSelector.IsEnabled = current is SnesSampleInstrument;
                snesDsp.IsVisible = kind == InstrumentKind.SnesSample;
                snesDsp.ShowInstrument(current as SnesSampleInstrument);
                clearSampleButton.IsVisible = current is SnesSampleInstrument { SampleData: not null };
                SetPresetSelection();
                RebuildInputs();
                UpdateWaveformAvailability();
                displayedInstrumentSnapshot = snapshot;
                displayedInstrument = current;
                displayedKind = kind;
            }
            finally
            {
                isRefreshing = false;
            }
        }

        /// <summary>選択トラックの識別記号と色を音色見出しへ反映する。</summary>
        public void ShowChannel(Track track)
        {
            channelLabel = ChannelPalette.GetShortLabel(track.Channel, track.ChannelIndex);
            kindLabel.Background = ChannelPalette.GetBrush(track.Channel, track.ChannelIndex);
            UpdateKindLabel(presenter.RequiredKind);
        }

        /// <summary>イベント購読と入力部品への参照を解除する。</summary>
        public void Dispose()
        {
            instrumentSelector.SelectionChanged -= OnInstrumentSelected;
            addButton.Click -= OnAddClicked;
            removeButton.Click -= OnRemoveClicked;
            applyButton.Click -= OnApplyClicked;
            importWavButton.Click -= OnImportWav;
            presetSelector.SelectionChanged -= OnPresetSelected;
            clearSampleButton.Click -= OnClearSample;
            snesDsp.NoiseChanged -= UpdateWaveformAvailability;
            snesDsp.Dispose();
            inputs.Clear();
            parameterPanel.Children.Clear();
            displayedInstrumentSnapshot = null;
            displayedInstrument = null;
            isBound = false;
        }

        private void UpdateKindLabel(InstrumentKind kind) => kindLabel.Text = $"{channelLabel} · {kind}";

        private void PopulatePresets()
        {
            presetItems.Clear();
            presetItems.Add(new ComboBoxItem { Content = "（合成波形）" });
            string category = string.Empty;
            foreach (SnesInstrumentPreset preset in presenter.SnesPresets)
            {
                if (category != preset.Category)
                {
                    category = preset.Category;
                    presetItems.Add(new ComboBoxItem { Content = category, IsEnabled = false });
                }
                ComboBoxItem item = new ComboBoxItem { Content = preset.Name, Tag = preset.Name };
                ToolTip.SetTip(item, preset.Description);
                presetItems.Add(item);
            }
            presetSelector.ItemsSource = presetItems;
        }

        private void SetPresetSelection()
        {
            string? presetName = (presenter.CurrentInstrument as SnesSampleInstrument)?.Preset;
            presetSelector.SelectedItem = presetItems.Find(item => item.IsEnabled && (string?)item.Tag == presetName);
        }

        private void OnPresetSelected(object? sender, SelectionChangedEventArgs arguments)
        {
            if (isRefreshing || presetSelector.SelectedItem is not ComboBoxItem { IsEnabled: true } item)
            {
                return;
            }
            execute(() => presenter.SelectPreset(item.Tag as string));
            // 拒否時も選択表示を確定済み音色へ戻し、未確定の他の入力は保持する。
            isRefreshing = true;
            try
            {
                SetPresetSelection();
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private void UpdateWaveformAvailability()
        {
            if (inputs.TryGetValue("waveform", out Control? waveform) && presenter.CurrentInstrument is SnesSampleInstrument sample)
            {
                waveform.IsEnabled = sample.Preset == null && sample.SampleData == null && !snesDsp.IsNoiseEnabled;
            }
        }

        private void RebuildInputs()
        {
            inputs.Clear();
            parameterPanel.Children.Clear();
            foreach (InstrumentParameter parameter in presenter.GetParameters())
            {
                StackPanel row = new StackPanel { Spacing = FieldSpacing };
                row.Children.Add(new TextBlock { Text = parameter.Label });
                Control input = parameter.Choices.Length == 0
                    ? new TextBox { Text = parameter.Value }
                    : new ComboBox { ItemsSource = parameter.Choices, SelectedItem = parameter.Value,
                        HorizontalAlignment = HorizontalAlignment.Stretch };
                input.Classes.Add("numeric");
                inputs.Add(parameter.Key, input);
                row.Children.Add(input);
                parameterPanel.Children.Add(row);
            }
        }

        private int FindInstrumentIndex(int? instrumentId)
        {
            for (int index = 0; index < instruments.Count; index++)
            {
                if (instruments[index].Id == instrumentId)
                {
                    return index;
                }
            }
            return -1;
        }

        private void OnInstrumentSelected(object? sender, SelectionChangedEventArgs arguments)
        {
            int index = instrumentSelector.SelectedIndex;
            if (isRefreshing || index < 0 || index >= instruments.Count)
            {
                return;
            }
            execute(() => presenter.SelectInstrument(instruments[index].Id));
        }

        private void OnAddClicked(object? sender, RoutedEventArgs arguments) => execute(presenter.AddInstrument);
        private void OnRemoveClicked(object? sender, RoutedEventArgs arguments) => execute(presenter.RemoveInstrument);
        private void OnClearSample(object? sender, RoutedEventArgs arguments) => execute(presenter.ClearSample);
        private void OnImportWav(object? sender, RoutedEventArgs arguments) =>
            WavImportRequested?.Invoke(rootNoteInput.Text ?? string.Empty, loopSampleInput.IsChecked == true);

        private void OnApplyClicked(object? sender, RoutedEventArgs arguments)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (KeyValuePair<string, Control> input in inputs)
            {
                values.Add(input.Key, input.Value switch
                {
                    TextBox textBox => textBox.Text ?? string.Empty,
                    ComboBox comboBox => comboBox.SelectedItem as string ?? string.Empty,
                    _ => throw new InvalidOperationException("未対応の入力部品です。")
                });
            }
            SnesInstrumentInput? snesInput = presenter.CurrentInstrument is SnesSampleInstrument ? snesDsp.ReadInput() : null;
            execute(() => presenter.Apply(nameInput.Text ?? string.Empty, values, snesInput));
        }

        private TControl RequireControl<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"XAML に {name} がありません。");
    }
}
