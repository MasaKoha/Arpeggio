using Arpeggio.Daw.Presenters;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>実デバイスなしで再生・停止・構造変更の寿命を検証する。</summary>
    public sealed class TransportPresenterTests
    {
        private const int BufferFrames = 512;
        private const int ChangedTempo = 180;
        private const int ShortSongLength = 48;
        private const int FramesBeyondSongEnd = 44100;

        /// <summary>描画用ポーリングは合成せず、出力要求だけが再生位置を進める。</summary>
        [Fact]
        public void PlaybackPositionAdvancesOnlyWhenAudioRequestsBuffer()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.TogglePlayback();
            Assert.True(fixture.View.IsPlaying);
            Assert.True(fixture.Audio.IsRunning);
            fixture.Presenter.Poll();
            Assert.Equal(0, fixture.Audio.CallbackCount);
            Assert.Equal(0d, fixture.View.PositionTick);

            Assert.Equal(BufferFrames, fixture.Audio.RequestFrames(BufferFrames));
            fixture.Presenter.Poll();
            Assert.True(fixture.View.PositionTick > 0);
            fixture.Presenter.Transport.TogglePlayback();
            Assert.False(fixture.View.IsPlaying);
            Assert.False(fixture.Audio.IsRunning);
            fixture.Presenter.Transport.Stop();
            Assert.Equal(0d, fixture.View.PositionTick);
            Assert.Equal("1:1:00", fixture.View.Position);
        }

        /// <summary>ドラッグ途中の公開ノートが、出力再起動なしで次のバッファから音を変える。</summary>
        [Fact]
        public void MovingActiveNoteChangesNextBufferWithoutRestartingAudio()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Add(0, 60);
            fixture.Presenter.Transport.TogglePlayback();
            float[] beforeMove = fixture.Audio.RequestSamples(BufferFrames);
            Assert.Contains(beforeMove, sample => sample != 0f);
            int startsBeforeMove = fixture.Audio.StartCount;
            int stopsBeforeMove = fixture.Audio.StopCount;

            pianoRoll.Press(1, 60, resizeToleranceTicks: 2, bypassSnap: false);
            pianoRoll.Drag(97, 64, bypassSnap: false);
            float[] duringDrag = fixture.Audio.RequestSamples(BufferFrames);
            Assert.All(duringDrag, sample => Assert.Equal(0f, sample));

            pianoRoll.Drag(1, 60, bypassSnap: false);
            float[] afterReturning = fixture.Audio.RequestSamples(BufferFrames);
            Assert.Contains(afterReturning, sample => sample != 0f);
            pianoRoll.EndDrag();
            Assert.Equal(startsBeforeMove, fixture.Audio.StartCount);
            Assert.Equal(stopsBeforeMove, fixture.Audio.StopCount);
            Assert.True(fixture.View.IsPlaying);
        }

        /// <summary>再生中のテンポ変更は出力を止めて再構築し、先頭から再開する。</summary>
        [Fact]
        public void TempoChangeStopsResetsAndResumesPlayback()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(BufferFrames);
            int startsBeforeChange = fixture.Audio.StartCount;
            int stopsBeforeChange = fixture.Audio.StopCount;
            fixture.Presenter.Transport.SetTempo(ChangedTempo);

            Assert.Equal(ChangedTempo, fixture.Document.Song.TempoBpm);
            Assert.Equal(ChangedTempo, fixture.View.TempoBpm);
            Assert.True(fixture.View.IsPlaying);
            Assert.Equal(startsBeforeChange + 1, fixture.Audio.StartCount);
            Assert.Equal(stopsBeforeChange + 1, fixture.Audio.StopCount);
            Assert.Equal(0d, fixture.View.PositionTick);
            fixture.Presenter.Undo();
            Assert.Equal(150, fixture.View.TempoBpm);
            Assert.True(fixture.View.IsPlaying);
        }

        /// <summary>非ループ曲の完走を次の表示更新で停止へ反映する。</summary>
        [Fact]
        public void FinishedPlaybackStopsOnPoll()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(ShortSongLength);
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(FramesBeyondSongEnd);
            fixture.Presenter.Poll();

            Assert.False(fixture.View.IsPlaying);
            Assert.False(fixture.Audio.IsRunning);
            Assert.Equal((double)ShortSongLength, fixture.View.PositionTick);
        }

        /// <summary>ループを有効にすると曲末を越えても再生を継続する。</summary>
        [Fact]
        public void LoopingPlaybackWrapsPositionAndKeepsPlaying()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(ShortSongLength);
            fixture.Presenter.Transport.ToggleLoop();
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(FramesBeyondSongEnd);
            fixture.Presenter.Poll();

            Assert.True(fixture.View.IsLooping);
            Assert.True(fixture.View.IsPlaying);
            Assert.InRange(fixture.View.PositionTick, 0d, (double)ShortSongLength);
        }

        /// <summary>小節・拍・tick の境界を固定 48 tick と 4/4 拍子で表示する。</summary>
        [Theory]
        [InlineData(0, "1:1:00")]
        [InlineData(47, "1:1:47")]
        [InlineData(48, "1:2:00")]
        [InlineData(192, "2:1:00")]
        [InlineData(205, "2:1:13")]
        public void PositionUsesMusicalBoundaries(double tick, string expected)
        {
            Assert.Equal(expected, TransportPresenter.FormatPosition(tick));
        }
    }
}
