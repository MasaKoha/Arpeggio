using System;
using System.Reactive.Disposables;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;

namespace Arpeggio.Daw.Audio.Sfx
{
    /// <summary>最新の生成済みスナップショットを先頭から一回、余白なしで試聴する。</summary>
    public sealed class SfxPreviewPlayer : IDisposable
    {
        /// <summary>試聴の固定サンプルレート。</summary>
        public const int SampleRate = 44100;
        private const int BufferFrames = 512;
        private const int StereoChannels = 2;
        private const int SinglePass = 1;
        private const double TailSeconds = 0;
        private const double FadeSeconds = 0.005;
        // 5msを超えないようフレーム数を切り捨てる。
        private const int FadeFrames = (int)(SampleRate * FadeSeconds);
        private readonly AudioOutputOwnership ownership;
        private readonly AudioOutputLease output;
        private readonly AudioCallback callback;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private readonly Subject<SfxPreviewRequest> requests = new Subject<SfxPreviewRequest>();
        private readonly BehaviorSubject<SfxPreviewState> states = new BehaviorSubject<SfxPreviewState>(SfxPreviewState.None);
        private SongRenderer? renderer;
        private long generation;
        private int fadePosition;
        private float volume = 1;
        private int finished;
        private Exception? callbackFailure;
        private bool isDisposed;
        private int publishedState;

        /// <summary>通常再生と同じ出力所有者を使い、テストでは準備処理だけを差し替える。</summary>
        public SfxPreviewPlayer(PlaybackEngine playback,
            Func<Song, CancellationToken, Task<SongRenderer>>? prepare = null)
        {
            ownership = playback.OutputOwnership;
            output = ownership.Preview;
            callback = RenderBuffer;
            Func<Song, CancellationToken, Task<SongRenderer>> prepareRenderer = prepare ?? PrepareAsync;
            subscriptions.Add(output.Interruptions.Subscribe(_ => Stop()));
            // Switch の内部ロックから所有権ロックへ直接入らず、入力側とのロック順逆転を防ぐ。
            subscriptions.Add(requests.Select(request => Prepare(request, prepareRenderer))
                .Switch().ObserveOn(TaskPoolScheduler.Default).Subscribe(completion => Adopt(completion.Request, completion.Renderer, completion.Failure)));
        }

        /// <summary>準備・再生・停止の状態通知。UI購読側でUIスケジューラーへ切り替える。</summary>
        public IObservable<SfxPreviewState> States => states.AsObservable();
        /// <summary>最新の試聴状態。</summary>
        public SfxPreviewState State => (SfxPreviewState)Volatile.Read(ref publishedState);
        /// <summary>最後に発生した準備・出力障害。</summary>
        public Exception? Failure { get; private set; }
        /// <summary>試聴専用の0〜1ゲイン。Songや書き出し設定へは渡さない。</summary>
        public float Volume
        {
            get => Volatile.Read(ref volume);
            set
            {
                if (!float.IsFinite(value) || value < 0 || value > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                Volatile.Write(ref volume, value);
            }
        }

        /// <summary>旧試聴を停止し、コピーした入力の最新世代だけを非同期で準備する。</summary>
        public void Play(Song snapshot)
        {
            lock (ownership.Gate)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                StopCore();
                Song isolated;
                try
                {
                    isolated = SongSerializer.Deserialize(SongSerializer.Serialize(snapshot));
                    output.Claim();
                }
                catch (Exception exception)
                {
                    Failure = exception;
                    PublishState(SfxPreviewState.Failed);
                    throw;
                }
                Failure = null;
                PublishState(SfxPreviewState.Generating);
                requests.OnNext(new SfxPreviewRequest(generation, isolated));
            }
        }

        /// <summary>生成待ちを含め世代を失効し、コールバック終了後に旧参照を解放する。</summary>
        public void Stop()
        {
            lock (ownership.Gate)
            {
                if (isDisposed) { return; }
                StopCore();
                PublishState(SfxPreviewState.None);
            }
        }

        /// <summary>既存UIの監視境界で完走・音声障害を停止へ反映する。</summary>
        public void Poll()
        {
            lock (ownership.Gate)
            {
                if (isDisposed || State != SfxPreviewState.Playing) { return; }
                Exception? failure = Volatile.Read(ref callbackFailure) ?? output.Failure;
                if (failure is not null)
                {
                    StopCore();
                    Failure = failure;
                    PublishState(SfxPreviewState.Failed);
                    return;
                }
                if (Volatile.Read(ref finished) != 0) { Stop(); }
            }
        }

        /// <summary>生成の取消・購読・音声参照を解放する。実デバイスは共有所有者が破棄する。</summary>
        public void Dispose()
        {
            lock (ownership.Gate)
            {
                if (isDisposed) { return; }
                StopCore();
                isDisposed = true;
                subscriptions.Dispose();
                requests.Dispose();
                PublishState(SfxPreviewState.Disposed);
                states.OnCompleted();
                states.Dispose();
            }
        }

        private void PublishState(SfxPreviewState state)
        {
            Volatile.Write(ref publishedState, (int)state);
            states.OnNext(state);
        }

        private void StopCore()
        {
            generation++;
            requests.OnNext(new SfxPreviewRequest(generation, null));
            output.Stop();
            renderer = null;
            Volatile.Write(ref finished, 0);
            Volatile.Write(ref callbackFailure, null);
        }

        private void Adopt(SfxPreviewRequest request, SongRenderer? prepared, Exception? failure)
        {
            lock (ownership.Gate)
            {
                if (isDisposed || request.Generation != generation) { return; }
                if (failure is not null)
                {
                    StopCore();
                    Failure = failure;
                    PublishState(SfxPreviewState.Failed);
                    return;
                }
                try
                {
                    renderer = prepared;
                    fadePosition = 0;
                    output.Start(SampleRate, BufferFrames, callback);
                    PublishState(SfxPreviewState.Playing);
                }
                catch (Exception exception)
                {
                    StopCore();
                    Failure = exception;
                    PublishState(SfxPreviewState.Failed);
                }
            }
        }

        private int RenderBuffer(Span<float> interleavedStereo)
        {
            // perf: JSON・生成・Reset・状態通知は行わず、事前構築したレンダラーとゲインだけを使う。
            if (Volatile.Read(ref callbackFailure) is not null)
            {
                interleavedStereo.Clear();
                return 0;
            }
            try
            {
                SongRenderer activeRenderer = renderer!;
                int writtenFrames = activeRenderer.Render(interleavedStereo);
                float gain = Volatile.Read(ref volume);
                for (int frame = 0; frame < writtenFrames; frame++)
                {
                    float fade = fadePosition < FadeFrames ? (float)fadePosition++ / FadeFrames : 1;
                    int sampleIndex = frame * StereoChannels;
                    interleavedStereo[sampleIndex] *= gain * fade;
                    interleavedStereo[sampleIndex + 1] *= gain * fade;
                }
                Volatile.Write(ref finished, activeRenderer.IsFinished ? 1 : 0);
                return writtenFrames;
            }
            catch (Exception exception)
            {
                interleavedStereo.Clear();
                Volatile.Write(ref callbackFailure, exception);
                return 0;
            }
        }

        private static IObservable<(SfxPreviewRequest Request, SongRenderer? Renderer, Exception? Failure)> Prepare(
            SfxPreviewRequest request, Func<Song, CancellationToken, Task<SongRenderer>> prepare)
        {
            Song? snapshot = request.Song;
            if (snapshot is null)
            {
                return Observable.Empty<(SfxPreviewRequest, SongRenderer?, Exception?)>();
            }
            return Observable.FromAsync(cancellation => prepare(snapshot, cancellation))
                .Select(renderer => (Request: request, Renderer: (SongRenderer?)renderer, Failure: (Exception?)null))
                .Catch<(SfxPreviewRequest Request, SongRenderer? Renderer, Exception? Failure), Exception>(exception =>
                    Observable.Return((Request: request, Renderer: (SongRenderer?)null, Failure: (Exception?)exception)));
        }

        private static Task<SongRenderer> PrepareAsync(Song snapshot, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var prepared = new SongRenderer(snapshot, new RenderSettings(SampleRate, SinglePass, TailSeconds));
                cancellation.ThrowIfCancellationRequested();
                return prepared;
            }, cancellation);
        }
    }
}
