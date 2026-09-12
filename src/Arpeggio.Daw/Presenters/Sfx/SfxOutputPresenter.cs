using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Audio.Sfx;

namespace Arpeggio.Daw.Presenters.Sfx
{
    /// <summary>候補スナップショットを解析・WAV 出力し、結果の revision を保持する。</summary>
    public sealed class SfxOutputPresenter : IDisposable
    {
        private readonly Subject<Unit> changes = new Subject<Unit>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool isDisposed;

        /// <summary>実行状態・結果の変更通知。</summary>
        public IObservable<Unit> Changes => changes.AsObservable();
        /// <summary>二重操作を抑止する実行状態。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>最後の解析または書き出し結果。</summary>
        public string Result { get; private set; } = string.Empty;
        /// <summary>結果を作った候補全体の識別子。</summary>
        public string? Revision { get; private set; }

        /// <summary>モニター処理を含まない、現在候補の音響解析を行う。</summary>
        public Task AnalyzeAsync(Song snapshot) => RunAsync(snapshot, (song, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalysisReport report = AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings());
            cancellationToken.ThrowIfCancellationRequested();
            return AnalysisTextRenderer.Render(report);
        });

        /// <summary>44100Hz・一回・tail0で新規WAVを保存する。既存ファイルを上書きしない。</summary>
        public Task ExportAsync(Song snapshot, string path) => RunAsync(snapshot, (song, cancellationToken) =>
        {
            var renderer = new SongRenderer(song, new RenderSettings(SfxPreviewPlayer.SampleRate, 1, 0));
            float[] samples = renderer.RenderAll();
            cancellationToken.ThrowIfCancellationRequested();
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WavWriter.Write(temporaryPath, samples, SfxPreviewPlayer.SampleRate);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, path, false);
            }
            finally
            {
                if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); }
            }
            return "WAV 保存済み: " + path;
        });

        /// <summary>画面終了後の結果通知を止め、保留処理を取り消す。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            changes.OnCompleted();
            changes.Dispose();
        }

        private async Task RunAsync(Song snapshot, Func<Song, CancellationToken, string> operation)
        {
            if (isDisposed || IsRunning) { return; }
            Song source = SongSerializer.Deserialize(SongSerializer.Serialize(snapshot));
            Revision = SfxHash.ComputeRevision(source);
            IsRunning = true;
            Result = "処理中…";
            changes.OnNext(Unit.Default);
            CancellationToken cancellationToken = lifetime.Token;
            try
            {
                string result = await Task.Run(() => operation(source, cancellationToken), cancellationToken);
                if (!isDisposed) { Result = result; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (!isDisposed) { Result = "処理失敗: " + exception.Message + " 再試行できます。"; }
            }
            finally
            {
                IsRunning = false;
                if (!isDisposed) { changes.OnNext(Unit.Default); }
            }
        }
    }
}
