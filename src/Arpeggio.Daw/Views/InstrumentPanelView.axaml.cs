using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Instruments;
using Arpeggio.Daw.Presenters;
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
        private readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>();
        private InstrumentPanelPresenter presenter = null!;
        private Action<Action> execute = null!;
        private IReadOnlyList<Instrument> instruments = Array.Empty<Instrument>();
        private IReadOnlyList<Instrument>? displayedInstrumentSnapshot;
        private Instrument? displayedInstrument;
        private InstrumentKind displayedKind;
        private bool isRefreshing;
        private bool isBound;

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
        }

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
            instrumentSelector.SelectionChanged += OnInstrumentSelected;
            addButton.Click += OnAddClicked;
            removeButton.Click += OnRemoveClicked;
            applyButton.Click += OnApplyClicked;
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
            if (ReferenceEquals(snapshot, displayedInstrumentSnapshot) &&
                ReferenceEquals(current, displayedInstrument) && kind == displayedKind)
            {
                return;
            }
            isRefreshing = true;
            try
            {
                instruments = presenter.Instruments;
                kindLabel.Text = kind.ToString();
                instrumentSelector.ItemsSource = instruments.Select(instrument => $"{instrument.Id}: {instrument.Name}").ToArray();
                instrumentSelector.SelectedIndex = FindInstrumentIndex(current?.Id);
                nameInput.Text = current?.Name ?? string.Empty;
                nameInput.IsEnabled = current != null;
                removeButton.IsEnabled = current != null;
                applyButton.IsEnabled = current != null;
                RebuildInputs();
                displayedInstrumentSnapshot = snapshot;
                displayedInstrument = current;
                displayedKind = kind;
            }
            finally
            {
                isRefreshing = false;
            }
        }

        /// <summary>イベント購読と入力部品への参照を解除する。</summary>
        public void Dispose()
        {
            instrumentSelector.SelectionChanged -= OnInstrumentSelected;
            addButton.Click -= OnAddClicked;
            removeButton.Click -= OnRemoveClicked;
            applyButton.Click -= OnApplyClicked;
            inputs.Clear();
            parameterPanel.Children.Clear();
            displayedInstrumentSnapshot = null;
            displayedInstrument = null;
            isBound = false;
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
            execute(() => presenter.Apply(nameInput.Text ?? string.Empty, values));
        }

        private TControl RequireControl<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"XAML に {name} がありません。");
    }
}
