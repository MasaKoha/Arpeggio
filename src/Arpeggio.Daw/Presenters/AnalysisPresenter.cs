using System;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>解析をワーカースレッドへ渡し、現在の編集内容に対応する結果だけを表示する。</summary>
    public sealed class AnalysisPresenter : IDisposable
    {
        private readonly DawDocument document;
        private readonly IMainWindowView view;
        private readonly Action changed;
        private readonly Func<Song, int?, CancellationToken, AnalysisReport> analyze;
        private CancellationTokenSource? cancellation;
        private string? sourceSnapshot;
        private Song? sourceSong;
        private bool isDisposed;

        /// <summary>画面境界と解析関数を明示的に受け取る。</summary>
        public AnalysisPresenter(DawDocument document, IMainWindowView view, Action changed,
            Func<Song, int?, CancellationToken, AnalysisReport>? analyze = null)
        {
            this.document = document;
            this.view = view;
            this.changed = changed;
            this.analyze = analyze ?? Analyze;
        }

        /// <summary>解析中は二重起動を拒否する。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>現在の編集内容に対する音響・合成警告の合計。</summary>
        public int WarningCount { get; private set; }
        /// <summary>最新の有効な解析結果。</summary>
        public string ResultText { get; private set; } = string.Empty;

        /// <summary>開始時点のソングを解析し、UI スレッドで完了・失敗を表示する。</summary>
        public async Task RunAsync(int? soloTrack = null)
        {
            if (isDisposed || IsRunning) { return; }
            using CancellationTokenSource operation = new CancellationTokenSource();
            cancellation = operation;
            IsRunning = true;
            WarningCount = 0;
            ResultText = string.Empty;
            view.ShowAnalysis("解析中…", true);
            changed();
            try
            {
                string snapshot = SongSerializer.Serialize(document.Song);
                sourceSnapshot = snapshot;
                sourceSong = document.Song;
                AnalysisReport report = await Task.Run(() =>
                    analyze(SongSerializer.Deserialize(snapshot), soloTrack, operation.Token), operation.Token).ConfigureAwait(false);
                operation.Token.ThrowIfCancellationRequested();
                string text = AnalysisTextRenderer.Render(report);
                await view.RunOnUiThreadAsync(() => Complete(report, text, operation)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                await view.RunOnUiThreadAsync(() =>
                {
                    if (!isDisposed && !operation.IsCancellationRequested)
                    {
                        view.ShowAnalysis($"解析失敗: {exception.Message}", false);
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                await view.RunOnUiThreadAsync(() =>
                {
                    cancellation = null;
                    IsRunning = false;
                    if (isDisposed) { return; }
                    if (operation.IsCancellationRequested) { view.ShowAnalysis("編集内容が変わりました。再解析してください。", false); }
                    changed();
                }).ConfigureAwait(false);
            }
        }

        /// <summary>選択変更では維持し、編集内容が変わったときだけ結果を無効化する。</summary>
        public void RefreshValidity()
        {
            if (sourceSnapshot == null) { return; }
            if (ReferenceEquals(sourceSong, document.Song) && sourceSnapshot == SongSerializer.Serialize(document.Song)) { return; }
            sourceSnapshot = null;
            sourceSong = null;
            cancellation?.Cancel();
            WarningCount = 0;
            ResultText = string.Empty;
            view.ShowAnalysis(IsRunning ? "解析中…（編集後の結果は破棄します）" : "編集内容が変わりました。再解析してください。", IsRunning);
        }

        /// <summary>画面終了後の通知を抑止する。実行中の Core 呼び出しは戻り次第破棄する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            cancellation?.Cancel();
        }

        private void Complete(AnalysisReport report, string text, CancellationTokenSource operation)
        {
            if (isDisposed || operation.IsCancellationRequested) { return; }
            RefreshValidity();
            if (operation.IsCancellationRequested) { return; }
            ResultText = text;
            WarningCount = (int)Math.Min(int.MaxValue,
                (long)report.Warnings.Count + report.RenderWarnings.Count + report.DroppedRenderWarningCount);
            view.ShowAnalysis(text, false);
        }

        private static AnalysisReport Analyze(Song song, int? soloTrack, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalysisReport report = AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings(), soloTrack);
            cancellationToken.ThrowIfCancellationRequested();
            return report;
        }
    }
}
