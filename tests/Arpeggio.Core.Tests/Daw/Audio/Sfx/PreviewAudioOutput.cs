using System;
using Arpeggio.Daw.Audio;

namespace Arpeggio.Core.Tests.Daw.Audio.Sfx
{
    /// <summary>割り当て済みバッファだけを要求し、出力所有者の切替順を検証する偽デバイス。</summary>
    internal sealed class PreviewAudioOutput : IAudioOutput
    {
        private AudioCallback? callback;
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal int SampleRate { get; private set; }
        internal bool IsRunning => callback is not null;
        internal bool FailStart { get; set; }

        /// <summary>出力の同時開始を拒否し、設定とコールバックを保持する。</summary>
        public void Start(int sampleRate, int bufferFrames, AudioCallback callback)
        {
            if (this.callback is not null) { throw new InvalidOperationException("同時出力"); }
            if (FailStart) { throw new InvalidOperationException("出力開始失敗"); }
            this.callback = callback;
            SampleRate = sampleRate;
            StartCount++;
        }
        /// <summary>実行中の出力だけを止める。</summary>
        public void Stop()
        {
            if (callback is null) { return; }
            callback = null;
            StopCount++;
        }
        /// <summary>実デバイスの破棄回数を記録する。</summary>
        public void Dispose() { Stop(); DisposeCount++; }
        internal int Request(Span<float> buffer) => callback!(buffer);
    }
}
