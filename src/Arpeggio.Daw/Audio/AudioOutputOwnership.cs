using System;

namespace Arpeggio.Daw.Audio
{
    /// <summary>通常再生と試聴を単一デバイスへ直列化し、待機中の相手も失効させる。</summary>
    internal sealed class AudioOutputOwnership : IDisposable
    {
        private readonly IAudioOutput output;
        private AudioOutputLease? active;
        private bool isDisposed;

        internal AudioOutputOwnership(IAudioOutput output)
        {
            this.output = output;
            Playback = new AudioOutputLease(this);
            Preview = new AudioOutputLease(this);
        }

        internal object Gate { get; } = new object();
        internal AudioOutputLease Playback { get; }
        internal AudioOutputLease Preview { get; }
        internal Exception? Failure => output.Failure;

        internal void Claim(AudioOutputLease owner)
        {
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                output.Stop();
                active = null;
                // 発音前の非同期準備にも失効を伝え、遅い完了が所有権を取り返さないようにする。
                AudioOutputLease other = ReferenceEquals(owner, Playback) ? Preview : Playback;
                other.Interrupt();
                active = owner;
            }
        }

        internal void Start(AudioOutputLease owner, int sampleRate, int bufferFrames, AudioCallback callback)
        {
            lock (Gate)
            {
                Claim(owner);
                try { output.Start(sampleRate, bufferFrames, callback); }
                catch
                {
                    output.Stop();
                    active = null;
                    throw;
                }
            }
        }

        internal void Stop(AudioOutputLease owner)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(active, owner)) { return; }
                output.Stop();
                active = null;
            }
        }

        /// <summary>両用途を失効させ、共有する実デバイスを一度だけ破棄する。</summary>
        public void Dispose()
        {
            lock (Gate)
            {
                if (isDisposed) { return; }
                output.Stop();
                active = null;
                Playback.Interrupt();
                Preview.Interrupt();
                isDisposed = true;
                Playback.Dispose();
                Preview.Dispose();
                output.Dispose();
            }
        }
    }
}
