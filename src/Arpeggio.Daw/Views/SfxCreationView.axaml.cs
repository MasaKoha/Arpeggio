using System;
using System.Linq;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Presenters;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw.Views
{
    /// <summary>非モーダルのプリセット一覧と保存先入力を表示する。</summary>
    public partial class SfxCreationView : UserControl, IDisposable
    {
        private readonly ComboBox presets;
        private readonly TextBlock description;
        private readonly TextBox destination;
        private readonly Button createButton;
        private SfxCreationPresenter presenter = null!;
        private Action<Action> execute = null!;

        /// <summary>XAML の入力部品を解決する。</summary>
        public SfxCreationView()
        {
            AvaloniaXamlLoader.Load(this);
            presets = Require<ComboBox>("PresetSelector");
            description = Require<TextBlock>("PresetDescription");
            destination = Require<TextBox>("DestinationInput");
            createButton = Require<Button>("CreateButton");
        }

        /// <summary>共有カタログと作成要求を接続する。</summary>
        public void Bind(SfxCreationPresenter creationPresenter, Action<Action> executeAction)
        {
            presenter = creationPresenter;
            execute = executeAction;
            presets.ItemsSource = presenter.Presets.Select(preset => preset.Name).ToArray();
            presets.SelectionChanged += OnPresetSelected;
            createButton.Click += OnCreate;
            presets.SelectedIndex = 0;
        }

        /// <summary>文書切替後のディレクトリを既定保存先へ反映する。</summary>
        public void ResetDestination()
        {
            SfxPresetDescription preset = presenter.Presets[presets.SelectedIndex];
            description.Text = preset.Description;
            destination.Text = presenter.DefaultPath(preset.Kind);
        }

        /// <summary>入力イベントを解除する。</summary>
        public void Dispose()
        {
            presets.SelectionChanged -= OnPresetSelected;
            createButton.Click -= OnCreate;
        }

        private void OnPresetSelected(object? sender, SelectionChangedEventArgs arguments)
        {
            if (presets.SelectedIndex >= 0) { ResetDestination(); }
        }
        private void OnCreate(object? sender, RoutedEventArgs arguments) => execute(() =>
            presenter.Create(presenter.Presets[presets.SelectedIndex].Kind, destination.Text ?? string.Empty));
        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
    }
}
