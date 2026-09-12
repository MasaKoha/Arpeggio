using System;
using System.Globalization;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters.Transport
{
    /// <summary>現在の曲に対する感想を、曲ファイルと独立したメモへ追記する。</summary>
    public sealed class ListeningNotePresenter : IDisposable
    {
        private const string FeedbackSuffix = ".feedback.txt";
        private const string TimestampFormat = "yyyy-MM-dd HH:mm";
        private const int MessageDurationSeconds = 3;
        private readonly DawDocument document;
        private readonly Subject<string> messages = new Subject<string>();
        private bool isDisposed;

        /// <summary>保存時に現在のパスを参照する文書を受け取る。</summary>
        public ListeningNotePresenter(DawDocument document)
        {
            this.document = document;
        }

        /// <summary>最後の保存失敗理由。成功後は空文字。</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>保存結果を三秒間表示し、次の結果で古い消去予約を取り消す。</summary>
        public IObservable<string> ObserveMessages(IScheduler scheduler) => messages.Select(message =>
            message.Length == 0 ? Observable.Return(string.Empty) : Observable.Return(message).Concat(
                Observable.Timer(TimeSpan.FromSeconds(MessageDurationSeconds), scheduler).Select(_ => string.Empty)))
            .Switch();

        /// <summary>本文をタイムスタンプ付きで追記し、成功した場合だけ true を返す。空白のみなら何もしない。</summary>
        public bool Save(string text)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string documentPath = document.Path;
            if (documentPath.Length == 0)
            {
                return ReportFailure("先に曲を保存してください。");
            }
            try
            {
                string timestamp = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
                File.AppendAllText(documentPath + FeedbackSuffix, $"[{timestamp}] {text}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return ReportFailure($"感想の保存に失敗しました: {exception.Message}");
            }
            Error = string.Empty;
            messages.OnNext("保存しました");
            return true;
        }

        /// <summary>一時表示の消去予約と結果通知を終了する。文書は所有元が破棄する。</summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            isDisposed = true;
            messages.OnNext(string.Empty);
            messages.OnCompleted();
            messages.Dispose();
        }

        private bool ReportFailure(string message)
        {
            Error = message;
            messages.OnNext(message);
            return false;
        }
    }
}
