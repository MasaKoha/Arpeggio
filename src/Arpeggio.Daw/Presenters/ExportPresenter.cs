using System;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Platform;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>書き出しの実行状態と通知を管理する。</summary>
    public sealed class ExportPresenter : IDisposable
    {
        private readonly DawDocument document;
        private readonly IMainWindowView view;
        private readonly Action changed;
        private readonly Action<Song, string, CancellationToken> export;
        private readonly Action<string> reveal;
        private CancellationTokenSource? cancellation;
        private bool isDisposed;

        /// <summary>編集対象・UI 境界・書き出し関数・フォルダを開く関数を明示的に結線する。</summary>
        public ExportPresenter(DawDocument document, IMainWindowView view, Action changed,
            Action<Song, string, CancellationToken>? export = null, Action<string>? reveal = null)
        {
            this.document = document;
            this.view = view;
            this.changed = changed;
            this.export = export ?? SongFileExporter.Write;
            this.reveal = reveal ?? FileRevealer.Reveal;
        }

        /// <summary>書き出しの二重起動を防ぐ実行状態。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>ステータスバーへ表示する最新の通知。</summary>
        public string StatusText { get; private set; } = string.Empty;
        /// <summary>最後に書き出しへ成功したファイル。未書き出しなら null。「フォルダを開く」の対象。</summary>
        public string? LastExportedPath { get; private set; }

        /// <summary>開始時のソングを別スレッドで書き出し、成功・失敗を UI へ返す。</summary>
        public async Task RunAsync(string path)
        {
            if (isDisposed || IsRunning) { return; }
            using CancellationTokenSource operation = new CancellationTokenSource();
            cancellation = operation;
            IsRunning = true;
            Publish($"書き出し中… {path}");
            try
            {
                string snapshot = SongSerializer.Serialize(document.Song);
                await Task.Run(() => export(SongSerializer.Deserialize(snapshot), path, operation.Token), operation.Token).ConfigureAwait(false);
                operation.Token.ThrowIfCancellationRequested();
                await view.RunOnUiThreadAsync(() =>
                {
                    LastExportedPath = path;
                    Publish($"書き出し完了: {path}");
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                await view.RunOnUiThreadAsync(() => Publish($"書き出し失敗: {exception.Message}")).ConfigureAwait(false);
            }
            finally
            {
                await view.RunOnUiThreadAsync(() =>
                {
                    cancellation = null;
                    IsRunning = false;
                    Publish(StatusText);
                }).ConfigureAwait(false);
            }
        }

        /// <summary>最後に書き出したファイルを OS のファイルブラウザで開く。未書き出しなら何もしない。</summary>
        public void RevealLastExport()
        {
            if (isDisposed || LastExportedPath is null)
            {
                return;
            }
            reveal(LastExportedPath);
        }

        /// <summary>保存先確定前のキャンセルと画面終了後の通知抑止を要求する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            cancellation?.Cancel();
        }

        private void Publish(string text)
        {
            if (isDisposed) { return; }
            StatusText = text;
            view.ShowExportStatus(text, IsRunning, LastExportedPath);
            changed();
        }
    }
}
