using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Daw.Presenters.Sfx
{
    /// <summary>未確定の各欄を保持し、一欄の訂正で別の不正入力を見失わないようにする。</summary>
    public sealed class SfxParameterForm : IDisposable
    {
        private readonly SfxEditorPresenter editor;
        private bool isDisposed;
        private readonly Dictionary<string, object> pendingValues = new Dictionary<string, object>();
        private readonly Dictionary<string, string> pendingText = new Dictionary<string, string>();
        private readonly Subject<string> keyboardPatches = new Subject<string>();
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();

        /// <summary>キー入力の150ms集約と、確定／取消後のバッファ破棄を接続する。</summary>
        public SfxParameterForm(SfxEditorPresenter editor, IScheduler scheduler)
        {
            this.editor = editor;
            editor.BindKeyboard(keyboardPatches, scheduler);
            subscriptions.Add(editor.Changes.Subscribe(_ =>
            {
                if (!editor.Model.HasGesture)
                {
                    pendingValues.Clear();
                    pendingText.Clear();
                }
            }));
        }

        /// <summary>未確定の文字列を保持し、最後の有効音だけを更新する。</summary>
        public void EditText(string path, string text)
        {
            pendingText[path] = text;
            PublishInput(path, SfxParameterInput.ParseText(text), false);
        }

        /// <summary>ドラッグ途中の値を即時反映し、履歴は増やさない。</summary>
        public void Drag(string path, object value)
        {
            pendingText.Remove(path);
            PublishInput(path, value, false);
        }

        /// <summary>キー連続入力を一操作へ集約する。</summary>
        public void Step(SfxParameterDescription description, object current, int direction, bool fine)
        {
            pendingText.Remove(description.Path);
            PublishInput(description.Path, SfxParameterInput.Step(description, current, direction, fine), true);
        }

        /// <summary>Home・End・ホイール等の値変更も、キーと同じ確定境界へ渡す。</summary>
        public void QueueValue(string path, object value)
        {
            pendingText.Remove(path);
            PublishInput(path, value, true);
        }

        /// <summary>選択値または初期値への変更を一操作として確定する。</summary>
        public void Select(string path, object value)
        {
            pendingText.Remove(path);
            PublishInput(path, value, false);
            editor.Commit();
        }

        /// <summary>選択操作が検証で止まった場合も、利用者が選んだ入力を表示する。</summary>
        public object Value(string path, object value) => pendingValues.TryGetValue(path, out object? pending) ? pending : value;

        /// <summary>不正入力も含めた編集中の表示文字列を返す。</summary>
        public string Text(string path, object value) => pendingText.TryGetValue(path, out string? text)
            ? text : SfxParameterInput.Format(value);

        /// <summary>購読とキー入力ストリームを解放する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            editor.Stop();
            subscriptions.Dispose();
            keyboardPatches.OnCompleted();
            keyboardPatches.Dispose();
        }

        private void PublishInput(string path, object value, bool keyboard)
        {
            pendingValues[path] = value;
            string patch = SfxParameterInput.CreatePatch(pendingValues);
            if (keyboard) { keyboardPatches.OnNext(patch); }
            else { editor.UpdatePatch(patch); }
        }
    }
}
