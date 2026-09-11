using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Arpeggio.Daw.Audio
{
    /// <summary>デバイスの破棄権を持たず、一つの再生用途の開始停止だけを仲裁へ渡す。</summary>
    internal sealed class AudioOutputLease : IAudioOutput
    {
        private readonly AudioOutputOwnership ownership;
        private readonly Subject<Unit> interruptions = new Subject<Unit>();
        private bool isDisposed;

        internal AudioOutputLease(AudioOutputOwnership ownership) => this.ownership = ownership;
        internal IObservable<Unit> Interruptions => interruptions.AsObservable();
        internal void Claim() => ownership.Claim(this);
        internal void Interrupt()
        {
            if (!isDisposed) { interruptions.OnNext(Unit.Default); }
        }

        /// <summary>共有デバイスの障害。</summary>
        public Exception? Failure => ownership.Failure;
        /// <summary>相手の要求を失効させて出力を開始する。</summary>
        public void Start(int sampleRate, int bufferFrames, AudioCallback callback)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            ownership.Start(this, sampleRate, bufferFrames, callback);
        }
        /// <summary>自身が所有する出力だけを止める。</summary>
        public void Stop() => ownership.Stop(this);
        /// <summary>用途の購読を解放し、実デバイスの寿命は共有所有者に残す。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            Stop();
            isDisposed = true;
            interruptions.OnCompleted();
            interruptions.Dispose();
        }
    }
}
