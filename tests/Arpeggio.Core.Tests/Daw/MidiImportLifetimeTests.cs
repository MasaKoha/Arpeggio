using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Daw.Presenters;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>MIDI の二重開始・キャンセル・終了時の遅延通知と保存境界を検証する。</summary>
    public sealed class MidiImportLifetimeTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        /// <summary>変換中は二重開始と保存・Open を拒否し、キャンセル後の遅い候補を公開しない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreparingRejectsOtherOperationsAndDiscardsCancelledResult(bool dispose)
        {
            using var fixture = new MidiImportDawFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            using var presenter = new MidiImportPresenter(fixture.Editor.Document, fixture.Editor.View,
                _ => throw new InvalidOperationException("Open を開始しない。"), import: (stream, options) =>
                {
                    Interlocked.Increment(ref calls);
                    started.SetResult();
                    if (!release.Wait(Timeout)) { throw new TimeoutException(); }
                    return MidiImporter.Import(stream, options);
                });
            Task running = presenter.PrepareAsync(fixture.Input());
            int displays;
            try
            {
                await started.Task.WaitAsync(Timeout);
                await presenter.PrepareAsync(fixture.Input());
                await presenter.SaveAsync();
                presenter.OpenSaved();
                presenter.InvalidateCandidate();
                Assert.Equal(1, calls);
                Assert.True(presenter.IsRunning);
                if (dispose) { presenter.Dispose(); }
                else { presenter.Cancel(); }
                displays = fixture.Editor.View.MidiImportDisplayCount;
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.Null(presenter.PreparedResult);
            Assert.Null(presenter.SavedPath);
            Assert.False(presenter.CanSave);
            Assert.False(presenter.IsRunning);
            Assert.False(File.Exists(fixture.DestinationPath));
            if (dispose) { Assert.Equal(displays, fixture.Editor.View.MidiImportDisplayCount); }
            else { Assert.Contains("キャンセル", presenter.StatusText); }
        }

        /// <summary>保存の二重開始を防ぎ、終了のキャンセルを新規保存 API まで渡して既存ファイルを保つ。</summary>
        [Fact]
        public async Task DisposeCancelsSaveAndPreservesExistingFiles()
        {
            using var fixture = new MidiImportDawFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            CancellationToken observed = default;
            using var presenter = new MidiImportPresenter(fixture.Editor.Document, fixture.Editor.View, _ => { },
                write: (result, path, cancellationToken, sourcePath) =>
                {
                    Interlocked.Increment(ref calls);
                    observed = cancellationToken;
                    started.SetResult();
                    if (!release.Wait(Timeout, cancellationToken)) { throw new TimeoutException(); }
                    return MidiSongFile.Write(result, path, cancellationToken: cancellationToken, sourcePath: sourcePath);
                });
            await presenter.PrepareAsync(fixture.Input());
            File.WriteAllText(fixture.DestinationPath, "keep");
            Task running = presenter.SaveAsync();
            int displays;
            try
            {
                await started.Task.WaitAsync(Timeout);
                await presenter.SaveAsync();
                await presenter.PrepareAsync(fixture.Input());
                Assert.Equal(1, calls);
                presenter.Dispose();
                displays = fixture.Editor.View.MidiImportDisplayCount;
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.True(observed.IsCancellationRequested);
            Assert.False(presenter.IsRunning);
            Assert.Equal(displays, fixture.Editor.View.MidiImportDisplayCount);
            Assert.Equal("keep", File.ReadAllText(fixture.DestinationPath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>UI キュー済みの候補公開も終了後には実行せず、破棄された終了通知から状態を解放する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DisposeSuppressesQueuedNotification(bool discardCleanup)
        {
            using var fixture = new MidiImportDawFixture();
            using var presenter = new MidiImportPresenter(fixture.Editor.Document, fixture.Editor.View, _ => { });
            var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            fixture.Editor.View.Dispatch = action =>
            {
                calls++;
                if (discardCleanup && calls == 1)
                {
                    action();
                    return Task.CompletedTask;
                }
                queued.TrySetResult(action);
                return completed.Task;
            };
            Task running = presenter.PrepareAsync(fixture.Input());
            try
            {
                Action notification = await queued.Task.WaitAsync(Timeout);
                int displays = fixture.Editor.View.MidiImportDisplayCount;
                presenter.Dispose();
                if (!discardCleanup) { notification(); }
                completed.SetResult();
                await running.WaitAsync(Timeout);
                Assert.Equal(displays, fixture.Editor.View.MidiImportDisplayCount);
                Assert.Null(presenter.PreparedResult);
                Assert.False(presenter.IsRunning);
            }
            finally
            {
                presenter.Dispose();
                completed.TrySetResult();
                fixture.Editor.View.Dispatch = null;
            }
        }

        /// <summary>文書切替で進行中の取り込みを取り消し、旧操作の候補を新文書へ持ち込まない。</summary>
        [Fact]
        public async Task DocumentSwitchCancelsPendingImport()
        {
            using var fixture = new MidiImportDawFixture();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var presenter = new MidiImportPresenter(fixture.Editor.Document, fixture.Editor.View, _ => { },
                import: (stream, options) =>
                {
                    started.SetResult();
                    if (!release.Wait(Timeout)) { throw new TimeoutException(); }
                    return MidiImporter.Import(stream, options);
                });
            Task running = presenter.PrepareAsync(fixture.Input());
            try
            {
                await started.Task.WaitAsync(Timeout);
                fixture.Editor.Document.Open(fixture.Editor.Path);
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.Null(presenter.PreparedResult);
            Assert.False(presenter.IsRunning);
            Assert.False(File.Exists(fixture.DestinationPath));
        }
    }
}
