using System;
using System.Collections.Generic;
using System.Text;
using System.Reactive.Linq;
using System.Threading;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sequencing;

namespace Arpeggio.Daw.Audio
{
    /// <summary>UI で再生寿命を管理し、音声スレッドのレンダラーから表示用状態を受け取る。</summary>
    public sealed class PlaybackEngine : IDisposable
    {
        private const int SampleRate = 44100;
        private const int BufferFrames = 512;
        private const int SinglePass = 1;
        private const double TailSeconds = 0.5;
        private readonly IAudioOutput audioOutput;
        private readonly IDisposable interruptionSubscription;
        private readonly AudioCallback renderCallback;
        private readonly RenderWarning[] publishedWarnings = new RenderWarning[RenderReport.WarningCapacity];
        private Song? song;
        private SongRenderer? renderer;
        private TickClock? tickClock;
        private long publishedPositionSamples;
        private long publishedDroppedWarnings;
        private int publishedWarningCount;
        private int publishedFinished;
        private Exception? renderFailure;
        private bool isDisposed;

        internal AudioOutputOwnership OutputOwnership { get; }

        /// <summary>出力の寿命を引き受け、コールバックを明示的に接続する。</summary>
        public PlaybackEngine(IAudioOutput audioOutput)
        {
            OutputOwnership = new AudioOutputOwnership(audioOutput);
            this.audioOutput = OutputOwnership.Playback;
            interruptionSubscription = OutputOwnership.Playback.Interruptions.Subscribe(_ => Stop());
            renderCallback = RenderBuffer;
        }

        /// <summary>再生要求が有効で、出力の停止をまだ完了していないか。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>ループ区間を繰り返すか。</summary>
        public bool IsLooping { get; private set; }

        /// <summary>最後に完了したバッファの位置を、ループ区間に折り返した tick で返す。</summary>
        public double PositionTick
        {
            get
            {
                if (song is null || tickClock is null)
                {
                    return 0;
                }
                double position = tickClock.SamplesToTick(Interlocked.Read(ref publishedPositionSamples));
                if (IsLooping && position >= song.LengthTicks)
                {
                    int loopLength = song.LengthTicks - song.LoopStartTick;
                    return song.LoopStartTick + (position - song.LengthTicks) % loopLength;
                }
                return Math.Min(position, song.LengthTicks);
            }
        }

        /// <summary>最後に完了したバッファまでの警告件数を返す。</summary>
        public int WarningCount => Volatile.Read(ref publishedWarningCount);

        /// <summary>出力を停止し、編集中のソング参照を保持するレンダラーを準備する。</summary>
        public void Load(Song document)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            Stop();
            int loopCount = IsLooping ? int.MaxValue : SinglePass;
            SongRenderer nextRenderer = new SongRenderer(document, new RenderSettings(SampleRate, loopCount, TailSeconds));
            song = document;
            renderer = nextRenderer;
            tickClock = new TickClock(document.TempoBpm, SampleRate);
            ClearPublishedState();
        }

        /// <summary>現在位置から再生する。完走後は先頭から再生する。</summary>
        public void Play()
        {
            lock (OutputOwnership.Gate)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                if (IsPlaying)
                {
                    return;
                }
                SongRenderer activeRenderer = RequireRenderer();
                if (activeRenderer.IsFinished)
                {
                    Reset();
                }
                Volatile.Write(ref renderFailure, null);
                audioOutput.Start(SampleRate, BufferFrames, renderCallback);
                IsPlaying = true;
            }
        }

        /// <summary>コールバックの完了を待って停止し、現在位置を保持する。</summary>
        public void Stop()
        {
            lock (OutputOwnership.Gate)
            {
                audioOutput.Stop();
                IsPlaying = false;
            }
        }

        /// <summary>再生回数設定を停止中に再構築し、再生中だった場合は先頭から再開する。</summary>
        public void SetLoop(bool isLooping)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (IsLooping == isLooping)
            {
                return;
            }
            bool shouldResume = IsPlaying;
            IsLooping = isLooping;
            if (song is null)
            {
                return;
            }
            Load(song);
            if (shouldResume)
            {
                Play();
            }
        }

        /// <summary>出力を停止してからテンポ・長さを取り直し、先頭へ戻す。</summary>
        public void Reset()
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            Stop();
            RequireRenderer().Reset();
            tickClock = new TickClock(RequireSong().TempoBpm, SampleRate);
            ClearPublishedState();
        }

        /// <summary>UI スレッドで完走を停止へ反映し、音声スレッドの障害を通知する。</summary>
        public void Poll()
        {
            if (!IsPlaying)
            {
                return;
            }
            Exception? failure = Volatile.Read(ref renderFailure) ?? audioOutput.Failure;
            if (failure != null)
            {
                Stop();
                throw new InvalidOperationException($"再生を停止しました: {failure.Message}", failure);
            }
            if (Volatile.Read(ref publishedFinished) != 0)
            {
                Stop();
            }
        }

        /// <summary>公開済みの値だけを使い、UI スレッドで警告一覧を組み立てる。</summary>
        public string GetWarningsText()
        {
            int warningCount = Volatile.Read(ref publishedWarningCount);
            StringBuilder text = new StringBuilder();
            for (int warningIndex = 0; warningIndex < warningCount; warningIndex++)
            {
                RenderWarning warning = publishedWarnings[warningIndex];
                text.Append("トラック ").Append(warning.TrackIndex + 1)
                    .Append(" / tick ").Append(warning.Tick)
                    .Append(" / ").Append(warning.Kind)
                    .Append(": ").Append(warning.RequestedValue)
                    .Append(" → ").Append(warning.ActualValue).AppendLine();
            }
            long droppedWarnings = Interlocked.Read(ref publishedDroppedWarnings);
            if (droppedWarnings > 0)
            {
                text.Append("保持上限を超えた警告: ").Append(droppedWarnings).AppendLine();
            }
            return text.Length == 0 ? "警告はありません。" : text.ToString();
        }

        /// <summary>出力を停止して所有リソースを破棄する。</summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            Stop();
            interruptionSubscription.Dispose();
            OutputOwnership.Dispose();
            isDisposed = true;
        }

        private int RenderBuffer(Span<float> interleavedStereo)
        {
            if (Volatile.Read(ref renderFailure) != null)
            {
                interleavedStereo.Clear();
                return 0;
            }
            try
            {
                SongRenderer activeRenderer = RequireRenderer();
                int writtenFrames = activeRenderer.Render(interleavedStereo);
                PublishRenderedState(activeRenderer);
                return writtenFrames;
            }
            catch (Exception exception)
            {
                interleavedStereo.Clear();
                Volatile.Write(ref renderFailure, exception);
                return 0;
            }
        }

        private void PublishRenderedState(SongRenderer activeRenderer)
        {
            RenderReport report = activeRenderer.Report;
            IReadOnlyList<RenderWarning> warnings = report.Warnings;
            int warningCount = warnings.Count;
            for (int warningIndex = publishedWarningCount; warningIndex < warningCount; warningIndex++)
            {
                publishedWarnings[warningIndex] = warnings[warningIndex];
            }
            // 公開済みの要素は停止・Reset まで上書きしないため、UI がロックなしで読み取れる。
            Volatile.Write(ref publishedWarningCount, warningCount);
            Interlocked.Exchange(ref publishedDroppedWarnings, report.DroppedWarningCount);
            Interlocked.Exchange(ref publishedPositionSamples, activeRenderer.PositionSamples);
            Volatile.Write(ref publishedFinished, activeRenderer.IsFinished ? 1 : 0);
        }

        private void ClearPublishedState()
        {
            Interlocked.Exchange(ref publishedPositionSamples, 0);
            Interlocked.Exchange(ref publishedDroppedWarnings, 0);
            Volatile.Write(ref publishedWarningCount, 0);
            Volatile.Write(ref publishedFinished, 0);
            Volatile.Write(ref renderFailure, null);
        }

        private SongRenderer RequireRenderer()
        {
            return renderer ?? throw new InvalidOperationException("再生するソングを先に読み込んでください。");
        }

        private Song RequireSong()
        {
            return song ?? throw new InvalidOperationException("再生するソングを先に読み込んでください。");
        }
    }
}
