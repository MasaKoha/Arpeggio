using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Platform;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;

namespace Arpeggio.Daw.Presenters.Export
{
    /// <summary>音声書き出しとチップ変換の診断・保存・非同期操作の寿命を管理する。</summary>
    public sealed class ExportPresenter : IDisposable
    {
        private readonly DawDocument document;
        private readonly IMainWindowView view;
        private readonly Action changed;
        private readonly Action<Song, string, CancellationToken> export;
        private readonly Action<string> reveal;
        private readonly Func<Song, ChipExportOptions, ChipExportPlan> prepareChip;
        private readonly Func<ChipExportPlan, string, bool, CancellationToken, string, bool> writeChip;
        private CancellationTokenSource? cancellation;
        private string? chipSourcePath;
        private bool overwriteConfirmed;
        private bool isDisposed;

        /// <summary>編集対象・UI 境界・保存関数を結線する。省略時は既存音声経路と共通チップ変換を使う。</summary>
        public ExportPresenter(DawDocument document, IMainWindowView view, Action changed,
            Action<Song, string, CancellationToken>? export = null, Action<string>? reveal = null,
            Func<Song, ChipExportOptions, ChipExportPlan>? prepareChip = null,
            Func<ChipExportPlan, string, bool, CancellationToken, string, bool>? writeChip = null)
        {
            this.document = document;
            this.view = view;
            this.changed = changed;
            this.export = export ?? SongFileExporter.Write;
            this.reveal = reveal ?? FileRevealer.Reveal;
            this.prepareChip = prepareChip ?? ChipExportService.Prepare;
            this.writeChip = writeChip ?? WriteChip;
            document.Opened += OnDocumentOpened;
        }

        /// <summary>診断・保存・音声書き出し間の二重起動を防ぐ実行状態。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>ステータスバーへ表示する最新の通知。</summary>
        public string StatusText { get; private set; } = string.Empty;
        /// <summary>最後に書き出しへ成功したファイル。「フォルダを開く」の対象。</summary>
        public string? LastExportedPath { get; private set; }
        /// <summary>OS ピッカーで確定したチップ書き出し先。未選択時は null。</summary>
        public string? ChipDestinationPath { get; private set; }
        /// <summary>保存先の拡張子に対応するチップ形式。</summary>
        public ConversionFormat SelectedChipFormat { get; private set; }
        /// <summary>診断済みの確定内容。曲の後編集を追従せず、この plan だけを保存する。</summary>
        public ChipExportPlan? PreparedChipPlan { get; private set; }
        /// <summary>方式の制限と位置付き診断の表示文字列。</summary>
        public string ChipReportText { get; private set; } = ChipExportReportText.InitialLimitations;
        /// <summary>診断済み内容を保存できるか。strict と全エラー数を含む。</summary>
        public bool CanSaveChip => !isDisposed && !IsRunning && PreparedChipPlan is { CanWrite: true };

        /// <summary>開始時のソングを別スレッドで従来の WAV / OGG 経路へ渡す。</summary>
        public Task RunAsync(string path)
        {
            return RunOperationAsync($"書き出し中… {path}", async cancellationToken =>
            {
                ClearChipDestination();
                string snapshot = SongSerializer.Serialize(document.Song);
                await Task.Run(() => export(SongSerializer.Deserialize(snapshot), path, cancellationToken), cancellationToken).ConfigureAwait(false);
                await NotifyAsync(() => CompleteExport(path), cancellationToken).ConfigureAwait(false);
            });
        }

        /// <summary>手入力の拡張子と入力パスを検証し、診断前に方式の制限を表示する。上書き許可は OS 確認後だけ渡す。</summary>
        public void SelectChipDestination(string path, bool overwrite = false)
        {
            if (isDisposed || IsRunning) { return; }
            ClearChipDestination();
            Publish("チップ書き出し先を確認しています。");
            ExportFileTypes.Validate(path, document.Song.Chip);
            ConversionFormat format = ExportFileTypes.GetChipFormat(path);
            if (format == ConversionFormat.None)
            {
                throw new ArgumentException("チップ書き出しには .nsf / .vgm を指定してください。", nameof(path));
            }
            string destination = Path.GetFullPath(path);
            if (string.Equals(destination, document.Path, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("入力と同じパスには書き出せません。", nameof(path));
            }
            ChipDestinationPath = destination;
            SelectedChipFormat = format;
            chipSourcePath = document.Path;
            overwriteConfirmed = overwrite;
            Publish("チップ書き出し設定を確認し、変換を確認してください。");
        }

        /// <summary>設定変更後に古い診断済み内容を保存させない。</summary>
        public void InvalidateChipPlan()
        {
            if (isDisposed || IsRunning) { return; }
            PreparedChipPlan = null;
            ChipReportText = ChipExportReportText.InitialLimitations;
            Publish("設定を変更しました。変換を確認してください。");
        }

        /// <summary>開始時のスナップショットを Prepare し、保存せず全警告・長さ・予定サイズを表示する。</summary>
        public Task PrepareChipAsync(ChipExportOptions options)
        {
            return RunOperationAsync("チップ変換を確認中…", async cancellationToken =>
            {
                PreparedChipPlan = null;
                ChipReportText = ChipExportReportText.InitialLimitations;
                RequireChipDestination();
                if (options.Format != SelectedChipFormat)
                {
                    throw new ArgumentException("設定の形式と保存先の拡張子が一致しません。", nameof(options));
                }
                string snapshot = SongSerializer.Serialize(document.Song);
                ChipExportPlan plan = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return prepareChip(SongSerializer.Deserialize(snapshot), options);
                }, cancellationToken).ConfigureAwait(false);
                await NotifyAsync(() =>
                {
                    PreparedChipPlan = plan;
                    ChipReportText = ChipExportReportText.Format(plan.Report);
                    Publish(plan.CanWrite ? "変換確認完了。診断した内容を「書き出す」で保存できます。" : "変換を保存できません。診断を確認してください。");
                }, cancellationToken).ConfigureAwait(false);
            });
        }

        /// <summary>診断時と同じ plan を保存する。保存失敗時は候補を保持し、成功時は二重保存を防ぐ。</summary>
        public Task SaveChipAsync()
        {
            if (!CanSaveChip) { return Task.CompletedTask; }
            return RunOperationAsync("チップ書き出し中…", async cancellationToken =>
            {
                string path = RequireChipDestination();
                string sourcePath = chipSourcePath!;
                ChipExportPlan plan = PreparedChipPlan!;
                bool written = await Task.Run(() => writeChip(plan, path, overwriteConfirmed, cancellationToken, sourcePath), cancellationToken).ConfigureAwait(false);
                if (!written) { throw new InvalidOperationException("変換エラーまたは strict 警告により保存できません。"); }
                await NotifyAsync(() =>
                {
                    PreparedChipPlan = null;
                    CompleteExport(path);
                }, cancellationToken).ConfigureAwait(false);
            });
        }

        /// <summary>最後に成功したファイルを OS のファイルブラウザで開く。</summary>
        public void RevealLastExport()
        {
            if (isDisposed || LastExportedPath is null) { return; }
            reveal(LastExportedPath);
        }

        /// <summary>操作をキャンセルし、画面終了後の結果公開・通知を抑止して購読を解除する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            document.Opened -= OnDocumentOpened;
            cancellation?.Cancel();
            ClearChipDestination();
        }

        private async Task RunOperationAsync(string status, Func<CancellationToken, Task> operation)
        {
            if (isDisposed || IsRunning) { return; }
            using var operationCancellation = new CancellationTokenSource();
            cancellation = operationCancellation;
            IsRunning = true;
            Publish(status);
            try
            {
                await operation(operationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                await NotifyAsync(() => Publish($"書き出し失敗: {exception.Message}"), operationCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!isDisposed)
                {
                    await view.RunOnUiThreadAsync(() =>
                    {
                        cancellation = null;
                        IsRunning = false;
                        Publish(StatusText);
                    }).ConfigureAwait(false);
                }
                // View が終了してキュー内の通知を破棄した場合も操作の所有状態を解放する。
                if (isDisposed)
                {
                    cancellation = null;
                    IsRunning = false;
                }
            }
        }

        private Task NotifyAsync(Action action, CancellationToken cancellationToken)
        {
            if (isDisposed || cancellationToken.IsCancellationRequested) { return Task.CompletedTask; }
            return view.RunOnUiThreadAsync(() =>
            {
                if (!isDisposed && !cancellationToken.IsCancellationRequested) { action(); }
            });
        }

        private string RequireChipDestination()
        {
            if (ChipDestinationPath is null || chipSourcePath != document.Path)
            {
                throw new InvalidOperationException("書き出し先を選び直してください。");
            }
            ExportFileTypes.Validate(ChipDestinationPath, document.Song.Chip);
            return ChipDestinationPath;
        }

        private void OnDocumentOpened(string path)
        {
            if (ChipDestinationPath is null) { return; }
            cancellation?.Cancel();
            ClearChipDestination();
            Publish("文書が切り替わりました。書き出し先を選び直してください。");
        }

        private void ClearChipDestination()
        {
            ChipDestinationPath = null;
            chipSourcePath = null;
            SelectedChipFormat = ConversionFormat.None;
            PreparedChipPlan = null;
            overwriteConfirmed = false;
            ChipReportText = ChipExportReportText.InitialLimitations;
        }

        private void CompleteExport(string path)
        {
            LastExportedPath = path;
            Publish($"書き出し完了: {path}");
        }

        private static bool WriteChip(ChipExportPlan plan, string path, bool overwrite, CancellationToken cancellationToken, string sourcePath)
        {
            return ChipExportService.Write(plan, path, overwrite, cancellationToken, sourcePath);
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
