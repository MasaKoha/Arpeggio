using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>非同期書き出しの形式分岐・成功失敗通知・所有寿命を検証する。</summary>
    public sealed class ExportPresenterTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private const int SignatureLength = 4;

        /// <summary>拡張子に対応するファイルを生成し、通知を次の Poll 後も維持する。</summary>
        [Theory]
        [InlineData(".wav", "RIFF")]
        [InlineData(".OGG", "OggS")]
        public async Task ExportWritesSelectedFormatAndPublishesSuccess(string extension, string signature)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(24);
            fixture.Presenter.PianoRoll.Add(0, 60);
            string path = Path.ChangeExtension(fixture.Path, extension);
            string snapshot = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;
            await fixture.Presenter.Export.RunAsync(path);
            Assert.Equal(signature, Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, SignatureLength));
            Assert.Contains("書き出し完了", fixture.View.ExportStatus);
            Assert.False(fixture.View.IsExporting);
            Assert.False(fixture.Presenter.Export.IsRunning);
            fixture.Presenter.Poll();
            Assert.Contains("書き出し完了", fixture.View.Status);
            Assert.Equal(snapshot, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
            Assert.Equal(0, fixture.Audio.CallbackCount);
        }

        /// <summary>保存失敗を通知し、同じ Presenter で再実行できる。</summary>
        [Fact]
        public async Task ExportFailurePublishesStatusAndAllowsRetry()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(24);
            string missingPath = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "missing", "song.wav");
            await fixture.Presenter.Export.RunAsync(missingPath);
            Assert.Contains("書き出し失敗", fixture.View.Status);
            Assert.False(fixture.View.IsExporting);
            Assert.False(fixture.Presenter.Export.IsRunning);
            string validPath = Path.ChangeExtension(fixture.Path, ".wav");
            await fixture.Presenter.Export.RunAsync(validPath);
            Assert.True(File.Exists(validPath));
            Assert.Contains("書き出し完了", fixture.View.ExportStatus);
        }

        /// <summary>非対応の拡張子では既存ファイルを変更しない。</summary>
        [Fact]
        public async Task UnsupportedExtensionPreservesExistingFile()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            string path = Path.ChangeExtension(fixture.Path, ".txt");
            File.WriteAllText(path, "existing");
            await fixture.Presenter.Export.RunAsync(path);
            Assert.Contains("書き出し失敗", fixture.View.Status);
            Assert.Equal("existing", File.ReadAllText(path));
        }

        /// <summary>二重起動を拒否し、処理中の編集から開始時点のソングを隔離する。</summary>
        [Fact]
        public async Task RunningExportRejectsSecondRequestAndUsesSnapshot()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            string captured = string.Empty;
            int calls = 0;
            using ExportPresenter presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                (song, path, cancellationToken) =>
                {
                    Interlocked.Increment(ref calls);
                    started.SetResult();
                    if (!release.Wait(Timeout, cancellationToken)) { throw new TimeoutException(); }
                    captured = SongSerializer.Serialize(song);
                });
            Task running = presenter.RunAsync("unused.wav");
            try
            {
                await started.Task.WaitAsync(Timeout);
                Assert.True(presenter.IsRunning);
                Assert.True(fixture.View.IsExporting);
                fixture.Presenter.PianoRoll.Add(0, 60);
                await presenter.RunAsync("second.wav");
                Assert.Equal(1, calls);
            }
            finally { release.Set(); }
            await running.WaitAsync(Timeout);
            Assert.Equal(before, captured);
            Assert.False(presenter.IsRunning);
        }

        /// <summary>画面終了時にキャンセルし、完了や失敗を閉じた View へ通知しない。</summary>
        [Fact]
        public async Task DisposeCancelsWithoutLateStatus()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using ExportPresenter presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                (song, path, cancellationToken) =>
                {
                    started.SetResult();
                    if (!release.Wait(Timeout, cancellationToken)) { throw new TimeoutException(); }
                });
            Task running = presenter.RunAsync("unused.wav");
            await started.Task.WaitAsync(Timeout);
            int displays = fixture.View.ExportDisplayCount;
            presenter.Dispose();
            await running.WaitAsync(Timeout);
            Assert.Equal(displays, fixture.View.ExportDisplayCount);
        }
    }
}
