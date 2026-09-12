using System;
using System.Globalization;
using System.Linq;
using Arpeggio.Core.Document;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Arpeggio.Daw.Presenters.PianoRoll;

namespace Arpeggio.Daw.Views.PianoRoll
{
    /// <summary>効果の一覧と共通入力欄を表示し、確定操作を PanelPresenter へ渡す。</summary>
    public partial class NotePanelView : UserControl, IDisposable
    {
        private readonly TextBlock summary;
        private readonly StackPanel editor;
        private readonly ListBox effects;
        private readonly ComboBox kind;
        private readonly TextBox value;
        private readonly StackPanel arpeggioInputs;
        private readonly TextBox firstSemitones;
        private readonly TextBox secondSemitones;
        private readonly Button addButton;
        private readonly Button updateButton;
        private readonly Button removeButton;
        private NotePanelPresenter? presenter;
        private Action<Action> execute = null!;
        private Note? displayedNote;
        private bool isRefreshing;

        /// <summary>固定数の入力部品を解決する。</summary>
        public NotePanelView()
        {
            AvaloniaXamlLoader.Load(this);
            summary = Require<TextBlock>("NoteSummary");
            editor = Require<StackPanel>("EffectEditor");
            effects = Require<ListBox>("EffectList");
            kind = Require<ComboBox>("KindSelector");
            value = Require<TextBox>("ValueInput");
            arpeggioInputs = Require<StackPanel>("ArpeggioInputs");
            firstSemitones = Require<TextBox>("FirstSemitones");
            secondSemitones = Require<TextBox>("SecondSemitones");
            addButton = Require<Button>("AddEffectButton");
            updateButton = Require<Button>("UpdateEffectButton");
            removeButton = Require<Button>("RemoveEffectButton");
            kind.ItemsSource = Enum.GetValues<NoteEffectKind>().Where(effect => effect != NoteEffectKind.None).ToArray();
            kind.SelectedItem = NoteEffectKind.PitchSlide;
        }

        /// <summary>編集先と非モーダルなエラー表示境界を接続する。</summary>
        public void Bind(NotePanelPresenter notePresenter, Action<Action> executeAction)
        {
            presenter = notePresenter;
            execute = executeAction;
            effects.SelectionChanged += OnEffectSelected;
            kind.SelectionChanged += OnKindSelected;
            addButton.Click += OnAdd;
            updateButton.Click += OnUpdate;
            removeButton.Click += OnRemove;
        }

        /// <summary>ノート参照が変わったときだけ入力を更新する。</summary>
        public void Refresh()
        {
            Note? note = presenter?.SelectedNote;
            if (ReferenceEquals(note, displayedNote)) { return; }
            isRefreshing = true;
            int previousIndex = effects.SelectedIndex;
            displayedNote = note;
            summary.Text = note == null ? "ノートを選択してください。" :
                $"tick {note.Tick} / 長さ {note.DurationTicks}\n{NoteName.Format(note.MidiNote)} / 音量 {note.Volume} / 音色 ID {note.InstrumentId}";
            // 未選択時は無効化ではなく非表示にし、右ペインの縦幅を音色パネルへ譲る
            editor.IsEnabled = note != null;
            editor.IsVisible = note != null;
            effects.ItemsSource = note?.Effects.Select(FormatEffect).ToArray() ?? Array.Empty<string>();
            effects.SelectedIndex = note != null && note.Effects.Length > 0
                ? Math.Clamp(previousIndex, 0, note.Effects.Length - 1) : -1;
            isRefreshing = false;
            ShowSelectedEffect();
        }

        /// <summary>全入力イベントを解除する。</summary>
        public void Dispose()
        {
            effects.SelectionChanged -= OnEffectSelected;
            kind.SelectionChanged -= OnKindSelected;
            addButton.Click -= OnAdd;
            updateButton.Click -= OnUpdate;
            removeButton.Click -= OnRemove;
            presenter = null;
        }

        private NotePanelPresenter PanelPresenter => presenter ?? throw new InvalidOperationException("ノートパネルを接続してください。");
        private NoteEffectKind SelectedKind => kind.SelectedItem is NoteEffectKind selected ? selected : NoteEffectKind.PitchSlide;
        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"{name} がありません。");
        private int ReadValue() => SelectedKind == NoteEffectKind.Arpeggio
            ? NotePanelPresenter.PackArpeggio(Parse(firstSemitones), Parse(secondSemitones)) : Parse(value);
        private static int Parse(TextBox input) => int.Parse(input.Text ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static string FormatEffect(NoteEffect effect) => effect.Kind == NoteEffectKind.Arpeggio
            ? $"{effect.Kind} / +{NotePanelPresenter.FirstArpeggioSemitones(effect.Value)}, +{NotePanelPresenter.SecondArpeggioSemitones(effect.Value)} (0x{effect.Value:X2})"
            : $"{effect.Kind} / {effect.Value}";
        private void OnEffectSelected(object? sender, SelectionChangedEventArgs arguments)
        {
            if (!isRefreshing) { ShowSelectedEffect(); }
        }
        private void ShowSelectedEffect()
        {
            int index = effects.SelectedIndex;
            bool hasSelection = displayedNote != null && index >= 0 && index < displayedNote.Effects.Length;
            updateButton.IsEnabled = hasSelection;
            removeButton.IsEnabled = hasSelection;
            if (!hasSelection) { return; }
            NoteEffect effect = displayedNote!.Effects[index];
            kind.SelectedItem = effect.Kind;
            value.Text = effect.Value.ToString(CultureInfo.InvariantCulture);
            if (effect.Kind == NoteEffectKind.Arpeggio)
            {
                firstSemitones.Text = $"+{NotePanelPresenter.FirstArpeggioSemitones(effect.Value)}";
                secondSemitones.Text = $"+{NotePanelPresenter.SecondArpeggioSemitones(effect.Value)}";
            }
        }
        private void OnKindSelected(object? sender, SelectionChangedEventArgs arguments)
        {
            arpeggioInputs.IsVisible = SelectedKind == NoteEffectKind.Arpeggio;
            value.IsVisible = !arpeggioInputs.IsVisible;
        }
        private void OnAdd(object? sender, RoutedEventArgs arguments) => execute(() =>
        {
            PanelPresenter.AddEffect(SelectedKind, ReadValue());
            effects.SelectedIndex = PanelPresenter.SelectedNote!.Effects.Length - 1;
        });
        private void OnUpdate(object? sender, RoutedEventArgs arguments) => execute(() => PanelPresenter.UpdateEffect(effects.SelectedIndex, SelectedKind, ReadValue()));
        private void OnRemove(object? sender, RoutedEventArgs arguments) => execute(() => PanelPresenter.RemoveEffect(effects.SelectedIndex));
    }
}
