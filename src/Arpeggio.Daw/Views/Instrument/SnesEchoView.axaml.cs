using System;
using System.Globalization;
using Arpeggio.Core.Document;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Arpeggio.Daw.Presenters.Instrument;

namespace Arpeggio.Daw.Views.Instrument
{
    /// <summary>エコー Flyout の入力と確定操作を Presenter へ接続する。</summary>
    public partial class SnesEchoView : UserControl, IDisposable
    {
        private readonly NumericUpDown delayInput;
        private readonly TextBox feedbackInput;
        private readonly TextBox volumeInput;
        private readonly ComboBox firSelector;
        private readonly Button applyButton;
        private SnesEchoPresenter echoPresenter = null!;
        private Action<Action> execute = null!;
        private SnesEchoSettings? displayedSettings;
        private bool isBound;

        /// <summary>XAML の入力部品を解決する。</summary>
        public SnesEchoView()
        {
            AvaloniaXamlLoader.Load(this);
            delayInput = Require<NumericUpDown>("DelayInput");
            feedbackInput = Require<TextBox>("FeedbackInput");
            volumeInput = Require<TextBox>("VolumeInput");
            firSelector = Require<ComboBox>("FirSelector");
            applyButton = Require<Button>("ApplyButton");
        }

        /// <summary>明示的に組み立てた Presenter とステータス表示境界を接続する。</summary>
        public void Bind(SnesEchoPresenter echoPresenter, Action<Action> execute)
        {
            if (isBound)
            {
                throw new InvalidOperationException("エコーパネルは接続済みです。");
            }
            this.echoPresenter = echoPresenter;
            this.execute = execute;
            isBound = true;
            firSelector.ItemsSource = echoPresenter.FirPresetNames;
            applyButton.Click += OnApply;
            Refresh();
        }

        /// <summary>履歴復元と文書切替を表示し、選択変更だけなら未確定入力を保つ。</summary>
        public void Refresh()
        {
            if (!isBound || ReferenceEquals(displayedSettings, echoPresenter.Settings))
            {
                return;
            }
            SnesEchoSettings settings = echoPresenter.Settings;
            delayInput.Value = settings.DelayMilliseconds;
            feedbackInput.Text = settings.Feedback.ToString(CultureInfo.InvariantCulture);
            volumeInput.Text = settings.Volume.ToString(CultureInfo.InvariantCulture);
            firSelector.SelectedItem = echoPresenter.CurrentFirPreset;
            displayedSettings = settings;
        }

        /// <summary>確定操作の購読を解除する。</summary>
        public void Dispose()
        {
            applyButton.Click -= OnApply;
            displayedSettings = null;
            isBound = false;
        }

        private void OnApply(object? sender, RoutedEventArgs arguments) => execute(Apply);

        private void Apply()
        {
            decimal delay = delayInput.Value ?? throw new ArgumentException("エコー遅延を入力してください。");
            if (delay != decimal.Truncate(delay))
            {
                throw new ArgumentException("エコー遅延は整数で指定してください。");
            }
            echoPresenter.Apply(decimal.ToInt32(delay),
                double.Parse(feedbackInput.Text ?? string.Empty, CultureInfo.InvariantCulture),
                double.Parse(volumeInput.Text ?? string.Empty, CultureInfo.InvariantCulture), firSelector.SelectedItem as string);
        }

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"XAML に {name} がありません。");
    }
}
