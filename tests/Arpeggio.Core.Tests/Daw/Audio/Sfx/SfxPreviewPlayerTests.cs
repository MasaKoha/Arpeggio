using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Audio.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Audio.Sfx
{
    /// <summary>偽デバイスで世代・所有権・tail0・コピー・ゲイン・破棄を検証する。</summary>
    public sealed class SfxPreviewPlayerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        private const int StereoChannels = 2;
        private const int ExpectedFrames = 2940;
        private const int FadeFrames = 220;

        /// <summary>短い試聴は44100Hz・一回・余白なしで通常レンダラーと同じ音を返す。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public async Task PreviewUsesTailZeroAndRepeatStartsAtBeginning(ChipKind chip)
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            using var player = new SfxPreviewPlayer(playback);
            Song song = CreateSong(chip);
            string original = SongSerializer.Serialize(song);
            float[] expected = new SongRenderer(song, new RenderSettings(44100, 1, 0)).RenderAll();
            player.Play(song);
            await WaitFor(player, SfxPreviewState.Playing);
            var actual = new float[ExpectedFrames * StereoChannels];
            Assert.Equal(ExpectedFrames, output.Request(actual));
            Assert.Equal(44100, output.SampleRate);
            Assert.Equal(expected.Length, actual.Length);
            for (int frame = 0; frame < FadeFrames; frame++)
            {
                float gain = (float)frame / FadeFrames;
                Assert.Equal(expected[frame * StereoChannels] * gain, actual[frame * StereoChannels]);
                Assert.Equal(expected[frame * StereoChannels + 1] * gain, actual[frame * StereoChannels + 1]);
            }
            Assert.Equal(expected.Skip(FadeFrames * StereoChannels), actual.Skip(FadeFrames * StereoChannels));
            Assert.Equal(0, output.Request(new float[StereoChannels]));
            Assert.Equal(0, actual[^1]);
            player.Poll();
            Assert.Equal(SfxPreviewState.None, player.State);
            Assert.False(output.IsRunning);
            player.Play(song);
            await WaitFor(player, SfxPreviewState.Playing);
            var repeated = new float[actual.Length];
            output.Request(repeated);
            Assert.Equal(actual, repeated);
            Assert.Equal(original, SongSerializer.Serialize(song));
        }

        /// <summary>遅い旧世代が後から完成しても、最新世代の音と開始回数を変更しない。</summary>
        [Fact]
        public async Task LatestGenerationWinsEvenWhenOldPreparationIgnoresCancellation()
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            var first = new TaskCompletionSource<SongRenderer>();
            var second = new TaskCompletionSource<SongRenderer>();
            CancellationToken firstCancellation = default;
            int preparations = 0;
            using var player = new SfxPreviewPlayer(playback, (snapshot, cancellation) =>
            {
                preparations++;
                if (preparations == 1) { firstCancellation = cancellation; return first.Task; }
                return second.Task;
            });
            Song firstSong = CreateSong(ChipKind.Nes);
            Song secondSong = CreateSong(ChipKind.GameBoy);
            player.Play(firstSong);
            player.Play(secondSong);
            Assert.True(firstCancellation.IsCancellationRequested);
            Assert.Equal(0, output.StartCount);
            second.SetResult(Renderer(secondSong));
            await WaitFor(player, SfxPreviewState.Playing);
            first.SetResult(Renderer(firstSong));
            await first.Task;
            var actual = new float[ExpectedFrames * StereoChannels];
            output.Request(actual);
            float[] expected = Renderer(secondSong).RenderAll();
            Assert.Equal(expected.Skip(FadeFrames * StereoChannels), actual.Skip(FadeFrames * StereoChannels));
            Assert.Equal(1, output.StartCount);
            Assert.Equal(2, preparations);
        }

        /// <summary>通常再生は準備中の試聴も失効させ、試聴開始は通常再生を再開しない。</summary>
        [Fact]
        public async Task PlaybackAndPreviewAreExclusiveIncludingPendingPreparation()
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            Song song = CreateSong(ChipKind.Nes);
            playback.Load(song);
            var pending = new TaskCompletionSource<SongRenderer>();
            CancellationToken token = default;
            using var player = new SfxPreviewPlayer(playback, (_, cancellation) => { token = cancellation; return pending.Task; });
            playback.Play();
            player.Play(song);
            Assert.False(playback.IsPlaying);
            Assert.False(output.IsRunning);
            playback.Play();
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(SfxPreviewState.None, player.State);
            pending.SetResult(Renderer(song));
            await pending.Task;
            Assert.True(playback.IsPlaying);
            Assert.Equal(2, output.StartCount);
            player.Play(song);
            await WaitFor(player, SfxPreviewState.Playing);
            Assert.False(playback.IsPlaying);
            player.Stop();
            Assert.False(playback.IsPlaying);
            Assert.False(output.IsRunning);
        }

        /// <summary>停止と終了で準備を取り消し、遅い完了は出力を開始しない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StopAndDisposeInvalidatePendingCompletion(bool dispose)
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            var pending = new TaskCompletionSource<SongRenderer>();
            CancellationToken token = default;
            using var player = new SfxPreviewPlayer(playback, (_, cancellation) => { token = cancellation; return pending.Task; });
            Song song = CreateSong(ChipKind.Nes);
            player.Play(song);
            if (dispose) { player.Dispose(); }
            else { player.Stop(); }
            Assert.True(token.IsCancellationRequested);
            pending.SetResult(Renderer(song));
            await pending.Task;
            Assert.Equal(0, output.StartCount);
            Assert.False(output.IsRunning);
            Assert.Equal(dispose ? SfxPreviewState.Disposed : SfxPreviewState.None, player.State);
            player.Dispose();
            Assert.Equal(0, output.DisposeCount);
            playback.Dispose();
            Assert.Equal(1, output.DisposeCount);
        }

        /// <summary>レンダラーに渡す入力は呼び出し元の変更から独立し、ゲインは保存へ漏れない。</summary>
        [Fact]
        public async Task SnapshotAndMonitorVolumeAreIsolatedFromDocument()
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            Song? captured = null;
            using var player = new SfxPreviewPlayer(playback, (snapshot, _) =>
            {
                captured = snapshot;
                return Task.FromResult(Renderer(snapshot));
            });
            Song song = CreateSong(ChipKind.Nes);
            string original = SongSerializer.Serialize(song);
            player.Volume = 0;
            player.Play(song);
            song.Tracks[0].Notes.Clear();
            await WaitFor(player, SfxPreviewState.Playing);
            Assert.Equal(original, SongSerializer.Serialize(captured!));
            var actual = new float[ExpectedFrames * StereoChannels];
            output.Request(actual);
            Assert.All(actual, sample => Assert.Equal(0, sample));
            Assert.Equal(original, SongSerializer.Serialize(captured!));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.Volume = float.NaN);
        }

        /// <summary>準備・出力失敗の後でも、次の要求を新しい世代として再試行できる。</summary>
        [Fact]
        public async Task FailureDoesNotTerminateRequestStream()
        {
            var output = new PreviewAudioOutput();
            using var playback = new PlaybackEngine(output);
            int attempts = 0;
            using var player = new SfxPreviewPlayer(playback, (snapshot, _) =>
                ++attempts == 1 ? Task.FromException<SongRenderer>(new InvalidOperationException("生成失敗")) : Task.FromResult(Renderer(snapshot)));
            Song song = CreateSong(ChipKind.Nes);
            player.Play(song);
            await WaitFor(player, SfxPreviewState.Failed);
            Assert.False(output.IsRunning);
            output.FailStart = true;
            player.Play(song);
            await WaitFor(player, SfxPreviewState.Failed);
            Assert.Contains("出力開始失敗", player.Failure!.Message);
            output.FailStart = false;
            player.Play(song);
            await WaitFor(player, SfxPreviewState.Playing);
            Assert.Equal(1, output.StartCount);
        }

        internal static Task<SfxPreviewState> WaitFor(SfxPreviewPlayer player, SfxPreviewState state) =>
            player.States.Where(current => current == state).FirstAsync().Timeout(TestTimeout).ToTask();

        internal static Song CreateSong(ChipKind chip)
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with
                {
                    Envelope = parameters.Tone.Envelope with { AttackSeconds = 0, SustainSeconds = 0, DecaySeconds = 0.05 }
                }
            };
            return SfxEditor.CreateCandidate(parameters, chip).Song;
        }

        private static SongRenderer Renderer(Song song) => new SongRenderer(song, new RenderSettings(44100, 1, 0));
    }
}
