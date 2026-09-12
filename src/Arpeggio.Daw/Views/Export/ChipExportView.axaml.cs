using System;
using System.Globalization;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Arpeggio.Daw.Presenters.Export;

namespace Arpeggio.Daw.Views.Export
{
    /// <summary>チップ書き出しの設定入力と非モーダルな診断・保存操作を接続する。</summary>
    public partial class ChipExportView : UserControl, IDisposable
    {
        private readonly TextBlock destinationLabel;
        private readonly StackPanel settingsPanel;
        private readonly StackPanel copyrightPanel;
        private readonly TextBox loopsInput;
        private readonly TextBox authorInput;
        private readonly TextBox copyrightInput;
        private readonly CheckBox strictInput;
        private readonly Button prepareButton;
        private readonly Button saveButton;
        private readonly TextBox reportText;
        private ExportPresenter exportPresenter = null!;

        /// <summary>固定の入力部品と診断欄を取得する。</summary>
        public ChipExportView()
        {
            AvaloniaXamlLoader.Load(this);
            destinationLabel = Require<TextBlock>("DestinationLabel");
            settingsPanel = Require<StackPanel>("SettingsPanel");
            copyrightPanel = Require<StackPanel>("CopyrightPanel");
            loopsInput = Require<TextBox>("LoopsInput");
            authorInput = Require<TextBox>("AuthorInput");
            copyrightInput = Require<TextBox>("CopyrightInput");
            strictInput = Require<CheckBox>("StrictInput");
            prepareButton = Require<Button>("PrepareButton");
            saveButton = Require<Button>("SaveButton");
            reportText = Require<TextBox>("ReportText");
        }

        /// <summary>設定変更・診断・保存要求を明示的に結線する。</summary>
        public void Bind(ExportPresenter presenter)
        {
            exportPresenter = presenter;
            loopsInput.TextChanged += OnTextChanged;
            authorInput.TextChanged += OnTextChanged;
            copyrightInput.TextChanged += OnTextChanged;
            strictInput.IsCheckedChanged += OnStrictChanged;
            prepareButton.Click += OnPrepare;
            saveButton.Click += OnSave;
            Refresh();
        }

        /// <summary>選択形式・制限・診断・実行状態を表示する。</summary>
        public void Refresh()
        {
            bool selected = exportPresenter.ChipDestinationPath != null;
            destinationLabel.Text = selected
                ? $"{exportPresenter.SelectedChipFormat}: {exportPresenter.ChipDestinationPath}"
                : "書き出しボタンで NSF / VGM の保存先を選択してください。";
            copyrightPanel.IsVisible = exportPresenter.SelectedChipFormat == ConversionFormat.Nsf;
            settingsPanel.IsEnabled = selected && !exportPresenter.IsRunning;
            prepareButton.IsEnabled = selected && !exportPresenter.IsRunning;
            saveButton.IsEnabled = exportPresenter.CanSaveChip;
            reportText.Text = exportPresenter.ChipReportText;
        }

        /// <summary>所有するイベント購読をすべて解除する。</summary>
        public void Dispose()
        {
            loopsInput.TextChanged -= OnTextChanged;
            authorInput.TextChanged -= OnTextChanged;
            copyrightInput.TextChanged -= OnTextChanged;
            strictInput.IsCheckedChanged -= OnStrictChanged;
            prepareButton.Click -= OnPrepare;
            saveButton.Click -= OnSave;
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs arguments) => exportPresenter.InvalidateChipPlan();
        private void OnStrictChanged(object? sender, RoutedEventArgs arguments) => exportPresenter.InvalidateChipPlan();

        private async void OnPrepare(object? sender, RoutedEventArgs arguments)
        {
            // 不正な数値も共通診断へ渡し、古い plan の保存を許さない。
            int.TryParse(loopsInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int loops);
            await exportPresenter.PrepareChipAsync(new ChipExportOptions
            {
                Format = exportPresenter.SelectedChipFormat, Loops = loops,
                Author = authorInput.Text ?? string.Empty,
                Copyright = exportPresenter.SelectedChipFormat == ConversionFormat.Nsf ? copyrightInput.Text ?? string.Empty : string.Empty,
                Strict = strictInput.IsChecked == true
            });
        }

        private async void OnSave(object? sender, RoutedEventArgs arguments) => await exportPresenter.SaveChipAsync();

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
    }
}
