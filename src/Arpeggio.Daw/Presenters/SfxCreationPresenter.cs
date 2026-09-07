using System;
using System.Collections.Generic;
using System.IO;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>カタログから新規ファイルを生成し、保存成功後に編集対象を切り替える。</summary>
    public sealed class SfxCreationPresenter
    {
        private readonly DawDocument document;
        private readonly Action prepareSwitch;
        private readonly Action<string> open;

        /// <summary>現在の保存先と、文書切替の既存経路を受け取る。</summary>
        public SfxCreationPresenter(DawDocument document, Action prepareSwitch, Action<string> open)
        {
            this.document = document;
            this.prepareSwitch = prepareSwitch;
            this.open = open;
        }

        /// <summary>CLI と共通の全プリセット一覧。</summary>
        public IReadOnlyList<SfxPresetDescription> Presets { get; } = SfxPresetCatalog.GetAll();

        /// <summary>現在のファイルと同じディレクトリの既定保存先。</summary>
        public string DefaultPath(SfxPresetKind kind) => Path.Combine(
            Path.GetDirectoryName(document.Path)!, $"sfx-{SfxPresetCatalog.Get(kind).Name}.arpeggio.json");

        /// <summary>未保存編集を保護してから CLI と同じ保存 API で作成する。</summary>
        public void Create(SfxPresetKind kind, string path)
        {
            prepareSwitch();
            SfxPresetFile.Create(path, document.Song.Chip, kind);
            open(path);
        }
    }
}
