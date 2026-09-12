using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Presenters.Sfx;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Arpeggio.Daw.Views.Sfx
{
    /// <summary>SFX の保存ピッカーと候補変更の検査を所有する。</summary>
    internal sealed class SfxFileActions : IDisposable
    {
        private readonly Control owner;
        private readonly SfxEditorPresenter editor;
        private readonly SfxOutputPresenter output;
        private readonly Subject<Unit> changes = new Subject<Unit>();
        private bool isDisposed;

        internal SfxFileActions(Control owner, SfxEditorPresenter editor, SfxOutputPresenter output)
        {
            this.owner = owner;
            this.editor = editor;
            this.output = output;
        }

        internal IObservable<Unit> Changes => changes.AsObservable();
        internal bool IsPicking { get; private set; }
        internal string Message { get; private set; } = string.Empty;

        internal Task SaveAsync() => PickAsync(false);
        internal Task ExportAsync() => PickAsync(true);

        /// <summary>ピッカー終了後の適用を止める。</summary>
        public void Dispose()
        {
            isDisposed = true;
            changes.OnCompleted();
            changes.Dispose();
        }

        private async Task PickAsync(bool wav)
        {
            if (isDisposed || IsPicking || output.IsRunning) { return; }
            editor.Commit();
            if (editor.Model.HasGesture) { return; }
            IsPicking = true;
            Message = string.Empty;
            changes.OnNext(Unit.Default);
            try
            {
                Song snapshot = editor.Model.Snapshot();
                string revision = SfxHash.ComputeRevision(snapshot);
                TopLevel window = TopLevel.GetTopLevel(owner) ?? throw new InvalidOperationException("保存先を選択する画面がありません。");
                string extension = wav ? "wav" : "arpeggio.json";
                using IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = wav ? "SFX を WAV へ新規保存" : "SFX 候補を新規保存",
                    SuggestedFileName = "sfx." + extension, DefaultExtension = extension,
                    FileTypeChoices = new[] { new FilePickerFileType(wav ? "WAV 音声" : "Arpeggio ソング") { Patterns = new[] { "*." + extension } } },
                    ShowOverwritePrompt = false
                });
                if (isDisposed || file is null) { return; }
                string path = file.TryGetLocalPath() ?? throw new IOException("ローカルファイルを指定してください。");
                if (revision != SfxHash.ComputeRevision(editor.Model.Snapshot()))
                {
                    throw new InvalidOperationException("候補が変わりました。保存先を選び直してください。");
                }
                if (wav) { await output.ExportAsync(snapshot, path); }
                else { editor.SaveNew(path); }
            }
            catch (Exception exception)
            {
                if (!isDisposed) { Message = "保存先の選択／保存に失敗しました: " + exception.Message; }
            }
            finally
            {
                IsPicking = false;
                if (!isDisposed) { changes.OnNext(Unit.Default); }
            }
        }
    }
}
