using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Storage;
using Arpeggio.Daw.Platform.Sfx;
using Arpeggio.Daw.Presenters.Sfx;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Arpeggio.Daw.Views.Sfx
{
    /// <summary>チップ別の項目順・グループロック・包絡を構成し、値更新でフォーカスを作り直さない。</summary>
    internal sealed class SfxParameterPanel : StackPanel, IDisposable
    {
        private const int NoiseEnvelopeOrder = 10;
        private static readonly string[] GroupOrder = { "音の構成", "トーン音程", "トーン音量", "音程変化・反復", "チップ固有音色", "ノイズ" };
        private readonly List<SfxParameterRow> rows = new List<SfxParameterRow>();
        private readonly Dictionary<CheckBox, string[]> groupLocks = new Dictionary<CheckBox, string[]>();
        private readonly CompositeDisposable groupSubscriptions = new CompositeDisposable();
        private readonly Subject<Unit> settingsChanges = new Subject<Unit>();
        private readonly SfxEditorPresenter editor;
        private readonly SfxParameterForm form;
        private readonly SfxUserSettings settings;
        private readonly SfxEnvelopeView toneEnvelope = new SfxEnvelopeView();
        private readonly SfxEnvelopeView noiseEnvelope = new SfxEnvelopeView();
        private ChipKind displayedChip;
        private bool refreshing;

        internal SfxParameterPanel(SfxEditorPresenter editor, SfxParameterForm form, SfxUserSettings settings)
        {
            this.editor = editor;
            this.form = form;
            this.settings = settings;
            Spacing = (double)Application.Current!.FindResource("Arpeggio.Space.Section")!;
        }

        internal IObservable<Unit> SettingsChanges => settingsChanges.AsObservable();

        internal void Show(Song song, SfxSynchronizationState synchronization, SfxSongCompilationResult? generation)
        {
            SfxParameters? parameters = synchronization.Parameters ?? synchronization.SavedParameters;
            IsVisible = parameters is not null;
            if (parameters is null) { return; }
            refreshing = true;
            try
            {
                if (displayedChip != song.Chip) { Build(song.Chip); }
                using JsonDocument serialized = JsonDocument.Parse(SongSerializer.Serialize(song));
                JsonElement values = serialized.RootElement.GetProperty("sfx").GetProperty("parameters");
                foreach (SfxParameterRow row in rows)
                {
                    bool isNoise = SfxParameterLayout.Group(row.ParameterPath) == "ノイズ";
                    row.Show(values, synchronization.Editable && (!isNoise || parameters.Noise.Enabled));
                }
                foreach (var group in groupLocks)
                {
                    group.Key.IsChecked = group.Value.All(path => editor.Locks.Contains(path));
                    group.Key.IsEnabled = synchronization.Editable;
                }
                toneEnvelope.Show(generation?.Curves.Tone?.Envelope);
                noiseEnvelope.Show(generation?.Curves.Noise);
            }
            finally { refreshing = false; }
        }

        /// <summary>グループ再構成用の購読と設定通知を解放する。</summary>
        public void Dispose()
        {
            groupSubscriptions.Dispose();
            settingsChanges.OnCompleted();
            settingsChanges.Dispose();
        }

        private void Build(ChipKind chip)
        {
            groupSubscriptions.Clear();
            rows.Clear();
            groupLocks.Clear();
            Children.Clear();
            // 再構成前に旧親から外し、チップ変更後も同じ包絡表示を使う。
            if (toneEnvelope.Parent is Panel toneParent) { toneParent.Children.Remove(toneEnvelope); }
            if (noiseEnvelope.Parent is Panel noiseParent) { noiseParent.Children.Remove(noiseEnvelope); }
            displayedChip = chip;
            foreach (string groupName in GroupOrder)
            {
                SfxParameterDescription[] descriptions = editor.Parameters.Where(description => SfxParameterLayout.Group(description.Path) == groupName)
                    .OrderBy(description => description.Path.StartsWith("noise.envelope.") ? NoiseEnvelopeOrder + SfxParameterLayout.Order(description.Path) : SfxParameterLayout.Order(description.Path)).ToArray();
                Children.Add(CreateGroup(groupName, descriptions));
            }
        }

        private Expander CreateGroup(string name, SfxParameterDescription[] descriptions)
        {
            var content = new StackPanel { Spacing = (double)Application.Current!.FindResource("Arpeggio.Space.Large")! };
            var expander = new Expander
            {
                Header = name, Content = content, IsExpanded = name != "音程変化・反復" || settings.RepeatExpanded,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AddLock(content, descriptions);
            foreach (SfxParameterDescription description in descriptions)
            {
                var row = new SfxParameterRow(description, editor, form);
                rows.Add(row);
                groupSubscriptions.Add(row);
                content.Children.Add(row);
            }
            if (name == "トーン音量") { content.Children.Add(toneEnvelope); }
            if (name == "ノイズ")
            {
                content.Children.Insert(0, new TextBlock { Text = "ノイズ OFF 中は入力できません。「音の構成」で有効にしてください。", TextWrapping = TextWrapping.Wrap });
                content.Children.Add(noiseEnvelope);
            }
            if (name == "チップ固有音色")
            {
                content.Children.Add(new TextBlock
                {
                    Text = displayedChip == ChipKind.Snes ? "内蔵周期波形。pulse は25%、square は50%。duty sweep は非対応。" : "デューティは12.5 / 25 / 50 / 75%の4段階に量子化されます。",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (name == "音程変化・反復")
            {
                groupSubscriptions.Add(expander.GetObservable(Expander.IsExpandedProperty).Skip(1).Subscribe(expanded =>
                {
                    settings.RepeatExpanded = expanded;
                    settingsChanges.OnNext(Unit.Default);
                }));
            }
            return expander;
        }

        private void AddLock(StackPanel content, SfxParameterDescription[] descriptions)
        {
            if (descriptions.Length == 0) { return; }
            var groupLock = new CheckBox { Content = "変異から保護" };
            string[] paths = descriptions.Select(description => description.Path).ToArray();
            groupLocks.Add(groupLock, paths);
            content.Children.Add(groupLock);
            groupSubscriptions.Add(groupLock.GetObservable(ToggleButton.IsCheckedProperty).Skip(1).Where(_ => !refreshing).Subscribe(locked =>
            {
                foreach (string path in paths) { editor.SetGroupLock(path, locked == true); }
            }));
        }
    }
}
