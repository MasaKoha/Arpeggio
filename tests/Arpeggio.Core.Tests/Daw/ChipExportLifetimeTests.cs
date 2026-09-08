using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>チップ変換の二重開始・スナップショット・終了キャンセルと遅延結果の抑止を検証する。</summary>
    public sealed class ChipExportLifetimeTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        /// <summary>Prepare 中は音声書き出しも含め二重開始を拒否し、開始時の曲だけを変換する。</summary>
        [Fact]
        public async Task PreparingRejectsOtherOperationsAndUsesInitialSnapshot()
        {
            using var fixture = new DawPresenterFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            string captured = string.Empty;
            int calls = 0;
            using var presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                export: (_, _, _) => throw new InvalidOperationException("音声書き出しは開始しない。"),
                prepareChip: (song, options) =>
                {
                    Interlocked.Increment(ref calls);
                    started.SetResult();
                    if (!release.Wait(Timeout)) { throw new TimeoutException(); }
                    captured = SongSerializer.Serialize(song);
                    return ChipExportService.Prepare(song, options);
                });
            string output = Path.ChangeExtension(fixture.Path, ".vgm");
            presenter.SelectChipDestination(output);
            Task running = presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            try
            {
                await started.Task.WaitAsync(Timeout);
                fixture.Presenter.PianoRoll.Add(0, 60);
                await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Nsf });
                await presenter.SaveChipAsync();
                await presenter.RunAsync(Path.ChangeExtension(fixture.Path, ".wav"));
                presenter.SelectChipDestination(Path.ChangeExtension(fixture.Path, ".nsf"));
                presenter.InvalidateChipPlan();
                Assert.Equal(output, presenter.ChipDestinationPath);
                Assert.True(presenter.IsRunning);
                Assert.Equal(1, calls);
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.Equal(before, captured);
            Assert.True(presenter.CanSaveChip);
            Assert.False(File.Exists(output));
        }

        /// <summary>Prepare を中断できない場合も、終了後の結果・保存・通知を公開しない。</summary>
        [Fact]
        public async Task DisposeDiscardsLatePreparationResultWithoutNotifications()
        {
            using var fixture = new DawPresenterFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                prepareChip: (song, options) =>
                {
                    started.SetResult();
                    if (!release.Wait(Timeout)) { throw new TimeoutException(); }
                    return ChipExportService.Prepare(song, options);
                });
            string output = Path.ChangeExtension(fixture.Path, ".vgm");
            presenter.SelectChipDestination(output);
            Task running = presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            int displays;
            int dispatches;
            try
            {
                await started.Task.WaitAsync(Timeout);
                displays = fixture.View.ExportDisplayCount;
                dispatches = fixture.View.DispatchCount;
                presenter.Dispose();
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            await presenter.SaveChipAsync();
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.Equal(displays, fixture.View.ExportDisplayCount);
            Assert.Equal(dispatches, fixture.View.DispatchCount);
            Assert.Null(presenter.PreparedChipPlan);
            Assert.Null(presenter.LastExportedPath);
            Assert.False(presenter.CanSaveChip);
            Assert.False(presenter.IsRunning);
            Assert.False(File.Exists(output));
        }

        /// <summary>保存中は二重保存と再診断を拒否し、終了キャンセルをファイル API まで渡す。</summary>
        [Fact]
        public async Task SavingRejectsOtherOperationsAndDisposePreservesDestination()
        {
            using var fixture = new DawPresenterFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int writeCalls = 0;
            CancellationToken observed = default;
            using var presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                writeChip: (plan, path, overwrite, cancellationToken, sourcePath) =>
                {
                    Interlocked.Increment(ref writeCalls);
                    observed = cancellationToken;
                    started.SetResult();
                    if (!release.Wait(Timeout, cancellationToken)) { throw new TimeoutException(); }
                    return ChipExportService.Write(plan, path, overwrite, cancellationToken, sourcePath);
                });
            string output = Path.ChangeExtension(fixture.Path, ".vgm");
            File.WriteAllText(output, "keep");
            presenter.SelectChipDestination(output, overwrite: true);
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            ChipExportPlan? originalPlan = presenter.PreparedChipPlan;
            Task running = presenter.SaveChipAsync();
            int displays;
            try
            {
                await started.Task.WaitAsync(Timeout);
                await presenter.SaveChipAsync();
                await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
                presenter.InvalidateChipPlan();
                Assert.Equal(1, writeCalls);
                Assert.Same(originalPlan, presenter.PreparedChipPlan);
                displays = fixture.View.ExportDisplayCount;
                presenter.Dispose();
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.True(observed.IsCancellationRequested);
            Assert.Equal(displays, fixture.View.ExportDisplayCount);
            Assert.Equal("keep", File.ReadAllText(output));
            Assert.Null(presenter.LastExportedPath);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, "*.tmp"));
        }

        /// <summary>UI キュー内の完了通知を抑止し、View が終了通知を破棄しても実行状態を解放する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DisposeSuppressesAlreadyQueuedCompletion(bool discardCleanup)
        {
            using var fixture = new DawPresenterFixture();
            using var presenter = new ExportPresenter(fixture.Document, fixture.View, () => { });
            var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatchCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            presenter.SelectChipDestination(Path.ChangeExtension(fixture.Path, ".vgm"));
            int dispatchCalls = 0;
            fixture.View.Dispatch = action =>
            {
                dispatchCalls++;
                if (discardCleanup && dispatchCalls == 1)
                {
                    action();
                    return Task.CompletedTask;
                }
                queued.TrySetResult(action);
                return dispatchCompleted.Task;
            };
            Task running = presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            try
            {
                Action completion = await queued.Task.WaitAsync(Timeout);
                int displays = fixture.View.ExportDisplayCount;
                presenter.Dispose();
                if (!discardCleanup) { completion(); }
                dispatchCompleted.SetResult();
                await running.WaitAsync(Timeout);
                Assert.Equal(displays, fixture.View.ExportDisplayCount);
                Assert.Null(presenter.PreparedChipPlan);
                Assert.Null(presenter.LastExportedPath);
                Assert.False(presenter.IsRunning);
            }
            finally
            {
                presenter.Dispose();
                dispatchCompleted.TrySetResult();
                fixture.View.Dispatch = null;
            }
        }

        /// <summary>作業中の文書切替で旧文書の結果を破棄し、新文書への切替通知を上書きしない。</summary>
        [Fact]
        public async Task DocumentSwitchCancelsPendingPreparation()
        {
            using var fixture = new DawPresenterFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                prepareChip: (song, options) =>
                {
                    started.SetResult();
                    if (!release.Wait(Timeout)) { throw new TimeoutException(); }
                    return ChipExportService.Prepare(song, options);
                });
            presenter.SelectChipDestination(Path.ChangeExtension(fixture.Path, ".vgm"));
            Task running = presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            try
            {
                await started.Task.WaitAsync(Timeout);
                fixture.Document.Open(fixture.Path);
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.Null(presenter.PreparedChipPlan);
            Assert.Null(presenter.ChipDestinationPath);
            Assert.Contains("文書が切り替わりました", presenter.StatusText);
            Assert.False(presenter.IsRunning);
        }
    }
}
