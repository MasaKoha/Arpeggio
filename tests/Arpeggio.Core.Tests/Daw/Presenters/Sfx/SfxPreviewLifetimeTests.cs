using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Daw.Audio.Sfx;
using Arpeggio.Daw.Audio.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>画面の文書・履歴・タブ・終了と試聴の寿命を統合検証する。</summary>
    public sealed class SfxPreviewLifetimeTests
    {
        /// <summary>自動試聴OFFでは確定しても開始せず、手動試聴は通常再生を止める。</summary>
        [Fact]
        public async Task AutoPreviewOffStillAllowsManualExclusivePreview()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.AutoPreview = false;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            presenter.Commit();
            Assert.Equal(0, fixture.Audio.StartCount);
            fixture.Presenter.Transport.TogglePlayback();
            presenter.Play();
            await SfxPreviewPlayerTests.WaitFor(fixture.Presenter.SfxPreview, SfxPreviewState.Playing);
            Assert.False(fixture.Presenter.Transport.IsPlaying);
            fixture.Presenter.Transport.TogglePlayback();
            Assert.Equal(SfxPreviewState.None, fixture.Presenter.SfxPreview.State);
            Assert.True(fixture.Presenter.Transport.IsPlaying);
        }

        /// <summary>Undo・タブ離脱・文書切替は試聴を止め、復元だけで再開しない。</summary>
        [Theory]
        [InlineData("undo")]
        [InlineData("tab")]
        [InlineData("open")]
        [InlineData("external")]
        public async Task BoundaryStopsPreview(string boundary)
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.AutoPreview = false;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            presenter.Commit();
            presenter.Play();
            await SfxPreviewPlayerTests.WaitFor(fixture.Presenter.SfxPreview, SfxPreviewState.Playing);
            int starts = fixture.Audio.StartCount;
            switch (boundary)
            {
                case "undo": presenter.Undo(); break;
                case "tab": presenter.LeaveTab(); break;
                case "open": fixture.Presenter.Open(fixture.Path); break;
                case "external":
                    fixture.AddExternalNote(0, 60);
                    fixture.Presenter.ExternalFileChanged();
                    break;
            }
            Assert.Equal(SfxPreviewState.None, fixture.Presenter.SfxPreview.State);
            Assert.False(fixture.Audio.IsRunning);
            Assert.Equal(starts, fixture.Audio.StartCount);
        }

        /// <summary>終了時に試聴と通常再生が共有するデバイスまで解放する。</summary>
        [Fact]
        public async Task MainWindowDisposesActivePreviewAndSharedDevice()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            await SfxPreviewPlayerTests.WaitFor(fixture.Presenter.SfxPreview, SfxPreviewState.Playing);
            fixture.Presenter.Dispose();
            Assert.True(fixture.Audio.IsDisposed);
            Assert.False(fixture.Audio.IsRunning);
            Assert.Equal(SfxPreviewState.Disposed, fixture.Presenter.SfxPreview.State);
        }
    }
}
