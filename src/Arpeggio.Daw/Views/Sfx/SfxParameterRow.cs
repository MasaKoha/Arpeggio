using System;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Daw.Editing.Sfx;
using Arpeggio.Daw.Presenters.Sfx;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Arpeggio.Daw.Views.Sfx
{
    /// <summary>一つのパラメータの数値・選択・ドラッグ入力を表示する。</summary>
    internal sealed class SfxParameterRow : UserControl, IDisposable
    {
        private readonly SfxParameterDescription description;
        private readonly SfxEditorPresenter editor;
        private readonly SfxParameterForm form;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox number = new TextBox();
        private readonly Slider slider = new Slider();
        private readonly ComboBox choice = new ComboBox();
        private readonly CheckBox enabled = new CheckBox();
        private object current;
        private bool refreshing;
        private bool dragging;

        internal SfxParameterRow(SfxParameterDescription description, SfxEditorPresenter editor, SfxParameterForm form)
        {
            this.description = description;
            this.editor = editor;
            this.form = form;
            current = description.DefaultValue;
            Classes.Add("sfx-parameter");
            var panel = new StackPanel { Spacing = Resource<double>("Arpeggio.Space.Small") };
            var label = new TextBlock { Text = SfxParameterLayout.Label(description), TextWrapping = TextWrapping.Wrap };
            ToolTip.SetTip(label, description.Description);
            var valueLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*," + Resource<double>("Arpeggio.Sfx.NumericWidth").ToString(CultureInfo.InvariantCulture)) };
            valueLine.Children.Add(label);
            Control input = CreateInput();
            Grid.SetColumn(input, 1);
            valueLine.Children.Add(input);
            panel.Children.Add(valueLine);
            var reset = new Button { Content = "初期値へ" };
            AutomationProperties.SetName(reset, label.Text + "を初期値へ");
            var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            controls.Children.Add(slider);
            Grid.SetColumn(reset, 1);
            controls.Children.Add(reset);
            panel.Children.Add(controls);
            error.Foreground = Resource<IBrush>("Arpeggio.Danger");
            panel.Children.Add(error);
            Content = panel;
            subscriptions.Add(SfxViewEvents.Observe(reset, Button.ClickEvent).Subscribe(_ => form.Select(description.Path, description.DefaultValue)));
            BindSlider();
        }

        internal string ParameterPath => description.Path;

        internal void Show(JsonElement parameters, bool editable)
        {
            refreshing = true;
            try
            {
                current = SfxParameterInput.Read(parameters, description);
                number.Text = form.Text(description.Path, current);
                object selectedValue = form.Value(description.Path, current);
                enabled.IsChecked = selectedValue is true;
                choice.SelectedIndex = description.Choices.ToList().FindIndex(value => SfxParameterInput.Format(value) == SfxParameterInput.Format(selectedValue));
                if (slider.IsVisible)
                {
                    slider.Value = SfxParameterInput.ToSlider(description, Convert.ToDouble(current, CultureInfo.InvariantCulture));
                }
                IsEnabled = editable;
                bool invalid = editor.Model.State == SfxEditingState.Invalid &&
                    (editor.Model.ErrorPath == description.Path || description.Path.StartsWith(editor.Model.ErrorPath + ".", StringComparison.Ordinal));
                error.Text = invalid ? editor.Model.Error + " " + RangeText() : string.Empty;
                error.IsVisible = invalid;
                number.Classes.Set("invalid", invalid);
                if (!editor.Model.HasGesture) { dragging = false; }
                slider.Classes.Set("dragging", dragging);
            }
            finally { refreshing = false; }
        }

        /// <summary>入力の購読を解除する。</summary>
        public void Dispose() => subscriptions.Dispose();

        private Control CreateInput()
        {
            if (description.ValueKind == SfxParameterValueKind.Boolean)
            {
                enabled.Content = "有効";
                AutomationProperties.SetName(enabled, SfxParameterLayout.Label(description));
                subscriptions.Add(enabled.GetObservable(ToggleButton.IsCheckedProperty).Skip(1)
                    .Where(_ => !refreshing).Subscribe(value => form.Select(description.Path, value == true)));
                return enabled;
            }
            if (description.Choices.Count > 0)
            {
                choice.ItemsSource = description.Choices.Select(SfxParameterInput.Format).ToArray();
                choice.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                AutomationProperties.SetName(choice, SfxParameterLayout.Label(description));
                subscriptions.Add(choice.GetObservable(SelectingItemsControl.SelectedIndexProperty).Skip(1)
                    .Where(index => !refreshing && index >= 0)
                    .Subscribe(index => form.Select(description.Path, description.Choices[index])));
                subscriptions.Add(SfxViewEvents.Observe(choice, KeyDownEvent, RoutingStrategies.Tunnel).Subscribe(OnStepKey));
                return choice;
            }
            number.Classes.Add("numeric");
            AutomationProperties.SetName(number, SfxParameterLayout.Label(description));
            ToolTip.SetTip(number, RangeText() + "\n" + description.Description);
            subscriptions.Add(number.GetObservable(TextBox.TextProperty).Skip(1).Where(_ => !refreshing)
                .Subscribe(text => form.EditText(description.Path, text ?? string.Empty)));
            subscriptions.Add(SfxViewEvents.Observe(number, GotFocusEvent).Subscribe(_ => { editor.Commit(); editor.BeginGesture(); }));
            subscriptions.Add(SfxViewEvents.Observe(number, LostFocusEvent).Subscribe(_ => editor.Commit()));
            subscriptions.Add(SfxViewEvents.Observe(number, KeyDownEvent, RoutingStrategies.Tunnel).Subscribe(arguments =>
            {
                if (arguments.Key == Key.Enter)
                {
                    editor.Commit();
                    arguments.Handled = true;
                }
            }));
            return number;
        }

        private void BindSlider()
        {
            slider.IsVisible = description.Minimum.HasValue && description.Choices.Count == 0;
            if (!slider.IsVisible) { return; }
            slider.Minimum = SfxParameterInput.ToSlider(description, description.AllowsZero ? 0 : description.Minimum!.Value);
            slider.Maximum = SfxParameterInput.ToSlider(description, description.Maximum!.Value);
            slider.SmallChange = description.Step;
            AutomationProperties.SetName(slider, SfxParameterLayout.Label(description));
            subscriptions.Add(slider.GetObservable(RangeBase.ValueProperty).Skip(1).Where(_ => !refreshing)
                .Subscribe(position =>
                {
                    double value = SfxParameterInput.FromSlider(description, position);
                    if (dragging) { form.Drag(description.Path, value); }
                    else { form.QueueValue(description.Path, value); }
                }));
            subscriptions.Add(SfxViewEvents.Observe(slider, PointerPressedEvent, RoutingStrategies.Tunnel, true).Subscribe(_ =>
            {
                editor.Commit();
                editor.BeginGesture();
                dragging = true;
                slider.Classes.Add("dragging");
            }));
            subscriptions.Add(SfxViewEvents.Observe(slider, PointerReleasedEvent, RoutingStrategies.Bubble, true).Subscribe(_ => EndDrag()));
            subscriptions.Add(SfxViewEvents.Observe(slider, PointerCaptureLostEvent, RoutingStrategies.Direct | RoutingStrategies.Bubble, true).Subscribe(_ => EndDrag()));
            subscriptions.Add(SfxViewEvents.Observe(slider, KeyDownEvent, RoutingStrategies.Tunnel).Subscribe(OnStepKey));
        }

        private void EndDrag()
        {
            if (!dragging) { return; }
            dragging = false;
            slider.Classes.Remove("dragging");
            editor.Commit();
        }

        private void OnStepKey(KeyEventArgs arguments)
        {
            if (choice.IsDropDownOpen || arguments.KeyModifiers.HasFlag(KeyModifiers.Alt)) { return; }
            int direction = arguments.Key switch { Key.Left or Key.Down => -1, Key.Right or Key.Up => 1, _ => 0 };
            if (direction == 0) { return; }
            form.Step(description, current, direction, arguments.KeyModifiers.HasFlag(KeyModifiers.Shift));
            arguments.Handled = true;
        }

        private string RangeText() => description.Choices.Count > 0
            ? string.Join(" / ", description.Choices.Select(SfxParameterInput.Format))
            : FormattableString.Invariant($"範囲: {(description.AllowsZero ? "0 または " : string.Empty)}{description.Minimum:0.######}〜{description.Maximum:0.######} {description.Unit}");

        private static TResource Resource<TResource>(string key) => (TResource)Application.Current!.FindResource(key)!;
    }
}
