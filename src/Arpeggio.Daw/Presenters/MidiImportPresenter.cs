using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Daw.Editing;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>現在の編集から MIDI 候補を隔離し、確認・新規保存・明示 Open と操作寿命を管理する。</summary>
    public sealed class MidiImportPresenter : IDisposable
    {
        private readonly DawDocument document;
        private readonly IMainWindowView view;
        private readonly Action<string> open;
        private readonly Func<Stream, MidiImportOptions, MidiImportResult> import;
        private readonly Func<MidiImportResult, string, CancellationToken, string, MidiSongFileResult> write;
        private CancellationTokenSource? cancellation;
        private string sourcePath = string.Empty;
        private string destinationPath = string.Empty;
        private bool isDisposed;

        /// <summary>画面通知・保護付き Open と、テストで差し替え可能な変換／保存境界を接続する。</summary>
        public MidiImportPresenter(DawDocument document, IMainWindowView view, Action<string> open,
            Func<Stream, MidiImportOptions, MidiImportResult>? import = null,
            Func<MidiImportResult, string, CancellationToken, string, MidiSongFileResult>? write = null)
        {
            this.document = document;
            this.view = view;
            this.open = open;
            this.import = import ?? MidiImporter.Import;
            this.write = write ?? Write;
            document.Opened += OnDocumentOpened;
        }

        /// <summary>確認・保存の二重開始を拒否する実行状態。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>診断と同じ JSON を保持する候補。</summary>
        public MidiImportResult? PreparedResult { get; private set; }
        /// <summary>最後に新規保存できたパス。Open の失敗とは独立して保持する。</summary>
        public string? SavedPath { get; private set; }
        /// <summary>位置付き診断・統計・方式の制限。</summary>
        public string ReportText { get; private set; } = MidiImportReportText.InitialLimitations;
        /// <summary>最新の操作結果。</summary>
        public string StatusText { get; private set; } = string.Empty;
        /// <summary>候補を新規保存できるか。</summary>
        public bool CanSave => !isDisposed && !IsRunning && SavedPath is null && PreparedResult is { CanWrite: true };
        /// <summary>保存成功した JSON を別操作として開けるか。現文書の保護は Open 時に再検査する。</summary>
        public bool CanOpen => !isDisposed && !IsRunning && SavedPath != null;

        /// <summary>設定変更で古い候補と Open 対象を破棄する。保存済みファイルは削除しない。</summary>
        public void InvalidateCandidate()
        {
            if (isDisposed || IsRunning) { return; }
            ClearCandidate();
            Publish("設定を変更しました。変換を確認してください。");
        }

        /// <summary>入力を検証し、バックグラウンドで候補を作る。現文書・履歴・保存先は変更しない。</summary>
        public Task PrepareAsync(MidiImportInput input)
        {
            return RunOperationAsync("MIDI 変換を確認中…", async cancellationToken =>
            {
                ClearCandidate();
                ValidatePaths(input);
                MidiImportResult result = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MidiImportOptions options = CreateOptions(input);
                    cancellationToken.ThrowIfCancellationRequested();
                    using FileStream stream = File.OpenRead(sourcePath);
                    return import(stream, options);
                }, cancellationToken).ConfigureAwait(false);
                await NotifyAsync(() =>
                {
                    PreparedResult = result;
                    ReportText = MidiImportReportText.Format(result.Report);
                    Publish(result.CanWrite ? "候補を確認しました。「新規保存」で保存できます。" : "保存できません。診断を確認してください。");
                }, cancellationToken).ConfigureAwait(false);
            });
        }

        /// <summary>確認した同じ候補だけを保存する。競合・失敗時は再試行用に候補を保持する。</summary>
        public Task SaveAsync()
        {
            if (!CanSave) { return Task.CompletedTask; }
            return RunOperationAsync("MIDI 候補を新規保存中…", async cancellationToken =>
            {
                MidiImportResult result = PreparedResult!;
                string destination = destinationPath;
                string source = sourcePath;
                MidiSongFileResult saved = await Task.Run(() => write(result, destination, cancellationToken, source), cancellationToken).ConfigureAwait(false);
                if (!saved.Written) { throw new InvalidOperationException("変換エラーまたは strict 警告により保存できません。"); }
                await NotifyAsync(() =>
                {
                    SavedPath = destination;
                    Publish($"新規保存完了: {destination}。「開く」は別操作です。");
                }, cancellationToken).ConfigureAwait(false);
            });
        }

        /// <summary>保存成功とは独立して、現文書の未保存・競合保護を通して Open する。</summary>
        public void OpenSaved()
        {
            if (!CanOpen) { return; }
            string path = SavedPath!;
            try
            {
                open(path);
                Publish($"取り込んだ曲を開きました: {path}");
            }
            catch (Exception exception)
            {
                SavedPath = path;
                Publish($"新規保存は完了しています。開けません: {exception.Message}");
            }
        }

        /// <summary>実行中の操作を取り消し、候補を破棄する。確定済みファイルは保持する。</summary>
        public void Cancel()
        {
            if (isDisposed) { return; }
            cancellation?.Cancel();
            ClearCandidate();
            Publish("取り込みをキャンセルしました。確定済みファイルは保持します。");
        }

        /// <summary>終了後の遅い結果・通知を抑止し、文書購読を解除する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            document.Opened -= OnDocumentOpened;
            cancellation?.Cancel();
            ClearCandidate();
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
                await NotifyAsync(() => Publish($"取り込み失敗: {exception.Message}"), operationCancellation.Token).ConfigureAwait(false);
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

        private void ValidatePaths(MidiImportInput input)
        {
            if (string.IsNullOrWhiteSpace(input.SourcePath) || string.IsNullOrWhiteSpace(input.DestinationPath))
            {
                throw new ArgumentException("MIDI 入力と新規保存先を指定してください。");
            }
            sourcePath = Path.GetFullPath(input.SourcePath);
            destinationPath = Path.GetFullPath(input.DestinationPath);
            if (SamePath(sourcePath, destinationPath) || SamePath(document.Path, destinationPath) ||
                (!string.IsNullOrWhiteSpace(input.ChannelMapPath) && SamePath(input.ChannelMapPath, destinationPath)))
            {
                throw new ArgumentException("MIDI・map・現在の文書と同じパスには保存できません。");
            }
        }

        private static MidiImportOptions CreateOptions(MidiImportInput input)
        {
            int? tempo = string.IsNullOrWhiteSpace(input.Tempo) ? null : ParseInteger(input.Tempo, "基準テンポ");
            int quantizeTicks = ParseInteger(input.QuantizeTicks, "量子化幅");
            var report = new ConversionReport(ConversionFormat.Midi, input.Chip, input.Strict);
            var map = MidiImportChannelMapFile.Read(string.IsNullOrWhiteSpace(input.ChannelMapPath) ? null : input.ChannelMapPath, report);
            if (report.ErrorCount > 0) { throw new FormatException(report.Errors[0].Message); }
            return new MidiImportOptions
            {
                Chip = input.Chip, Tempo = tempo, QuantizeTicks = quantizeTicks, Polyphony = input.Polyphony,
                ChannelMap = map, SourceName = input.SourcePath,
                Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title, Strict = input.Strict
            };
        }

        private static int ParseInteger(string text, string label)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                throw new FormatException($"{label}は整数で指定してください。");
            }
            return number;
        }

        private static bool SamePath(string first, string second) =>
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

        private static MidiSongFileResult Write(MidiImportResult result, string path, CancellationToken cancellationToken, string sourcePath) =>
            MidiSongFile.Write(result, path, cancellationToken: cancellationToken, sourcePath: sourcePath);

        private void OnDocumentOpened(string path)
        {
            if (!IsRunning && PreparedResult is null && SavedPath is null) { return; }
            Cancel();
            Publish("文書が切り替わりました。必要なら変換を確認し直してください。");
        }

        private void ClearCandidate()
        {
            PreparedResult = null;
            SavedPath = null;
            ReportText = MidiImportReportText.InitialLimitations;
        }

        private void Publish(string text)
        {
            if (isDisposed) { return; }
            StatusText = text;
            view.ShowMidiImport();
        }
    }
}
