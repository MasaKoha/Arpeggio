using System;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Presenters.Brief
{
    /// <summary>Song と独立した作曲指示書の入力検証・保存・読み込み・コピーを管理する。</summary>
    public sealed class BriefEditorPresenter : IDisposable
    {
        private const int MessageDurationSeconds = 3;
        private readonly Subject<Unit> changes = new Subject<Unit>();
        private readonly Subject<string> messages = new Subject<string>();
        private bool hasInvalidTempoInput;
        private bool isDisposed;

        /// <summary>現在の入力を保持する不変の指示書。</summary>
        public CompositionBrief Brief { get; private set; } = new CompositionBrief { Title = CompositionBrief.DefaultTitle };

        /// <summary>最後に保存または読み込みが成功したファイル。</summary>
        public string? DocumentPath { get; private set; }

        /// <summary>操作名・修正位置・原因を含む画面用のエラー。</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>入力または操作結果が変わった通知。</summary>
        public IObservable<Unit> Changes => changes.AsObservable();

        /// <summary>成功表示を三秒後に消す。新しい操作は古い表示と消去予約を置き換える。</summary>
        public IObservable<string> ObserveMessages(IScheduler scheduler) => messages.Select(message =>
            message.Length == 0 ? Observable.Return(string.Empty) : Observable.Return(message).Concat(
                Observable.Timer(TimeSpan.FromSeconds(MessageDurationSeconds), scheduler).Select(_ => string.Empty)))
            .Switch();

        /// <summary>未入力のタイトルを既定値で補完して検証する。</summary>
        public void UpdateTitle(string? title) => Update(Brief with
        {
            Title = string.IsNullOrWhiteSpace(title) ? CompositionBrief.DefaultTitle : title
        });

        /// <summary>チップを更新する。null は指定なしを表す。</summary>
        public void UpdateChip(ChipKind? chip) => Update(Brief with { Chip = chip });

        /// <summary>空欄を未指定へ変換し、整数にできない入力が保存されることを防ぐ。</summary>
        public void UpdateTempo(string? text)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (string.IsNullOrWhiteSpace(text))
            {
                hasInvalidTempoInput = false;
                Update(Brief with { TempoBpm = null });
                return;
            }
            hasInvalidTempoInput = !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tempo);
            Update(hasInvalidTempoInput ? Brief : Brief with { TempoBpm = tempo });
        }

        /// <summary>雰囲気の改行と空白を保持して更新する。</summary>
        public void UpdateMood(string? text) => Update(Brief with { Mood = text });

        /// <summary>構成の改行と空白を保持して更新する。</summary>
        public void UpdateStructure(string? text) => Update(Brief with { Structure = text });

        /// <summary>声の役割の改行と空白を保持して更新する。</summary>
        public void UpdateInstrumentation(string? text) => Update(Brief with { Instrumentation = text });

        /// <summary>参考の改行と空白を保持して更新する。</summary>
        public void UpdateReferences(string? text) => Update(Brief with { References = text });

        /// <summary>制約の改行と空白を保持して更新する。</summary>
        public void UpdateConstraints(string? text) => Update(Brief with { Constraints = text });

        /// <summary>メモの改行と空白を保持して更新する。</summary>
        public void UpdateNotes(string? text) => Update(Brief with { Notes = text });

        /// <summary>検証済みの入力だけを原子的に保存し、成功時に保存先を保持する。</summary>
        public bool Save(string path) => TryExecute(() =>
        {
            ValidateInput();
            CompositionBriefFile.Save(Brief, path);
            DocumentPath = path;
        }, "保存しました", "保存に失敗しました");

        /// <summary>読み込みが成功した場合だけ、指示書・途中入力の状態・保存先を置き換える。</summary>
        public bool Open(string path) => TryExecute(() =>
        {
            CompositionBrief loaded = CompositionBriefFile.Load(path);
            Brief = loaded;
            DocumentPath = path;
            hasInvalidTempoInput = false;
        }, "読み込みました", "読み込みに失敗しました");

        /// <summary>現在の入力を検証し、AI に渡す整形テキストを返す。</summary>
        public string GetText()
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            ValidateInput();
            return CompositionBriefTextRenderer.Render(Brief);
        }

        /// <summary>OS への一回の書き込みが完了してからコピー成功を通知する。</summary>
        public async Task<bool> CopyTextAsync(Func<string, Task> writeClipboardText)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            messages.OnNext(string.Empty);
            try
            {
                string text = GetText();
                await writeClipboardText(text);
                if (isDisposed) { return false; }
                Error = string.Empty;
                changes.OnNext(Unit.Default);
                messages.OnNext("コピーしました");
                return true;
            }
            catch (Exception exception)
            {
                // OS のクリップボード実装によって失敗時の例外型が異なる。
                ReportFailure("コピーに失敗しました", exception);
                return false;
            }
        }

        /// <summary>ピッカーやクリップボードを含む操作失敗を画面へ通知する。</summary>
        public void ReportFailure(string operation, Exception exception)
        {
            if (isDisposed) { return; }
            Error = exception is CompositionBriefException briefException
                ? $"{operation}: {briefException.Code} · {briefException.ParameterPath} · {briefException.Message}"
                : $"{operation}: {exception.Message}";
            messages.OnNext(string.Empty);
            changes.OnNext(Unit.Default);
        }

        /// <summary>通知を終了し、非同期コピーの遅延完了を無効にする。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            messages.OnNext(string.Empty);
            changes.OnCompleted();
            messages.OnCompleted();
            changes.Dispose();
            messages.Dispose();
        }

        private void Update(CompositionBrief updatedBrief)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            Brief = updatedBrief;
            TryExecute(ValidateInput, string.Empty, "入力を確認してください");
        }

        private void ValidateInput()
        {
            if (hasInvalidTempoInput)
            {
                throw new CompositionBriefException("InvalidParameter", "tempoBpm", "テンポは正の整数または空欄で指定してください。");
            }
            CompositionBriefValidator.Validate(Brief);
        }

        private bool TryExecute(Action operation, string successMessage, string failureMessage)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            messages.OnNext(string.Empty);
            try
            {
                operation();
                Error = string.Empty;
                changes.OnNext(Unit.Default);
                messages.OnNext(successMessage);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ReportFailure(failureMessage, exception);
                return false;
            }
        }
    }
}
