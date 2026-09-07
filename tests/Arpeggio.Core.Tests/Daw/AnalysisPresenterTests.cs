using System;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>解析の表示境界・二重起動・編集競合・終了時のキャンセルを検証する。</summary>
    public sealed class AnalysisPresenterTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        /// <summary>Core の解析テキストを表示し、音響警告件数をステータスへ反映する。</summary>
        [Fact]
        public async Task CompletionPublishesAnalysisTextAndWarnings()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(24);
            int history = fixture.Document.Session.History.UndoCount;
            AnalysisReport expected = AudioAnalysisSource.AnalyzeSong(fixture.Document.Song, new AnalysisSettings());

            await fixture.Presenter.Analysis.RunAsync();

            Assert.Equal(AnalysisTextRenderer.Render(expected), fixture.View.AnalysisText);
            Assert.False(fixture.View.IsAnalyzing);
            Assert.False(fixture.Presenter.Analysis.IsRunning);
            Assert.True(fixture.View.DispatchCount > 0);
            Assert.True(expected.Warnings.Count > 0);
            Assert.Equal(expected.Warnings.Count + expected.RenderWarnings.Count, fixture.View.WarningCount);
            fixture.Presenter.Poll();
            Assert.Equal(fixture.Presenter.Analysis.WarningCount, fixture.View.WarningCount);
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
            Assert.Equal(0, fixture.Audio.CallbackCount);
        }

        /// <summary>ソロ指定は複製ソングだけに適用し、元のミュートや履歴を変えない。</summary>
        [Fact]
        public async Task SoloAnalysisPreservesEditingState()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(24);
            fixture.Presenter.PianoRoll.Add(0, 69);
            fixture.Presenter.ToggleMute(0);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;
            AnalysisReport expected = AudioAnalysisSource.AnalyzeSong(fixture.Document.Song, new AnalysisSettings(), track: 0);
            await fixture.Presenter.Analysis.RunAsync(0);
            Assert.Equal(AnalysisTextRenderer.Render(expected), fixture.View.AnalysisText);
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>再生中の警告へ解析の音響・合成警告を加算し、編集後は解析分だけを消す。</summary>
        [Fact]
        public async Task AnalysisWarningsAreAddedToPlaybackWarnings()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(24);
            fixture.Presenter.PianoRoll.SelectTrack(2);
            fixture.Presenter.Instruments.AddInstrument();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.Presenter.PianoRoll.ChangeVolume(-8);
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(512);
            fixture.Presenter.Poll();
            int playbackWarnings = fixture.View.WarningCount;
            Assert.True(playbackWarnings > 0);
            await fixture.Presenter.Analysis.RunAsync();
            Assert.True(fixture.Presenter.Analysis.WarningCount > 0);
            Assert.Equal(playbackWarnings + fixture.Presenter.Analysis.WarningCount, fixture.View.WarningCount);
            fixture.Presenter.ShowWarnings();
            Assert.Contains("合成 TriangleVolumeIgnored", fixture.View.Warnings);
            fixture.Presenter.Notes.AddEffect(NoteEffectKind.PitchSlide, 1);
            Assert.Equal(playbackWarnings, fixture.View.WarningCount);
        }

        /// <summary>ワーカーの完了を待たずに UI へ戻り、実行中の二重起動を拒否する。</summary>
        [Fact]
        public async Task RunningAnalysisRejectsSecondRequest()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            using AnalysisPresenter presenter = new AnalysisPresenter(fixture.Document, fixture.View, () => { },
                (song, track, cancellationToken) =>
                {
                    Interlocked.Increment(ref calls);
                    started.SetResult();
                    if (!release.Wait(Timeout, cancellationToken)) { throw new TimeoutException(); }
                    return new AnalysisReport();
                });
            Task running = presenter.RunAsync();
            try
            {
                await started.Task.WaitAsync(Timeout);
                Assert.True(presenter.IsRunning);
                Assert.True(fixture.View.IsAnalyzing);
                Assert.Equal("解析中…", fixture.View.AnalysisText);
                await presenter.RunAsync(0);
                Assert.Equal(1, calls);
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.False(presenter.IsRunning);
        }

        /// <summary>画面破棄時にワーカーへキャンセルを通知し、その後の表示を抑止する。</summary>
        [Fact]
        public async Task DisposeCancelsWithoutPublishingLateResult()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TaskCompletionSource<CancellationToken> started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            using AnalysisPresenter presenter = new AnalysisPresenter(fixture.Document, fixture.View, () => { },
                (song, track, cancellationToken) =>
                {
                    started.SetResult(cancellationToken);
                    if (!release.Wait(Timeout, cancellationToken)) { throw new TimeoutException(); }
                    return new AnalysisReport();
                });
            Task running = presenter.RunAsync();
            CancellationToken token = await started.Task.WaitAsync(Timeout);
            int displays = fixture.View.AnalysisDisplayCount;
            presenter.Dispose();
            await running.WaitAsync(Timeout);
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(displays, fixture.View.AnalysisDisplayCount);
            Assert.Equal(string.Empty, presenter.ResultText);
        }

        /// <summary>解析中に編集された場合は開始時点の結果を公開しない。</summary>
        [Fact]
        public async Task EditInvalidatesRunningSnapshot()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Song? captured = null;
            using AnalysisPresenter presenter = new AnalysisPresenter(fixture.Document, fixture.View, () => { },
                (song, track, cancellationToken) =>
                {
                    captured = song;
                    started.SetResult();
                    if (!release.Wait(Timeout)) { throw new TimeoutException(); }
                    return new AnalysisReport();
                });
            Task running = presenter.RunAsync();
            try
            {
                await started.Task.WaitAsync(Timeout);
                fixture.Presenter.PianoRoll.Add(0, 60);
                presenter.RefreshValidity();
                Assert.Empty(captured!.Tracks[0].Notes);
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.Equal(string.Empty, presenter.ResultText);
            Assert.Equal(0, presenter.WarningCount);
            Assert.False(fixture.View.IsAnalyzing);
            Assert.Contains("再解析", fixture.View.AnalysisText);
        }

        /// <summary>失敗時も入力を再度有効にして次の解析を受け付ける。</summary>
        [Fact]
        public async Task FailureAllowsRetry()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            int calls = 0;
            using AnalysisPresenter presenter = new AnalysisPresenter(fixture.Document, fixture.View, () => { },
                (song, track, cancellationToken) => ++calls == 1
                    ? throw new InvalidOperationException("analysis failure") : new AnalysisReport());
            await presenter.RunAsync();
            Assert.Contains("解析失敗: analysis failure", fixture.View.AnalysisText);
            Assert.False(fixture.View.IsAnalyzing);
            await presenter.RunAsync();
            Assert.Contains("RMS", fixture.View.AnalysisText);
            Assert.Equal(2, calls);
        }

        /// <summary>選択変更では結果を保持し、文書の再オープンでは無効化する。</summary>
        [Fact]
        public async Task SelectionKeepsResultButDocumentSwitchInvalidatesIt()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            await fixture.Presenter.Analysis.RunAsync();
            string result = fixture.View.AnalysisText;
            fixture.Presenter.PianoRoll.SelectTrack(1);
            Assert.Equal(result, fixture.View.AnalysisText);
            fixture.Presenter.Open(fixture.Path);
            Assert.Equal(0, fixture.View.WarningCount);
            Assert.Equal(string.Empty, fixture.Presenter.Analysis.ResultText);
        }
    }
}
