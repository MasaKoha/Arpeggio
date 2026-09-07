using System;
using Arpeggio.Daw.Audio;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>実デバイスなしで音声要求と開始停止を観測する。</summary>
    internal sealed class FakeAudioOutput : IAudioOutput
    {
        private const int StereoChannels = 2;
        private AudioCallback? callback;

        /// <summary>開始要求の回数。</summary>
        public int StartCount { get; private set; }
        /// <summary>稼働中の出力が停止された回数。</summary>
        public int StopCount { get; private set; }
        /// <summary>コールバックを要求できる状態か。</summary>
        public bool IsRunning => callback != null;
        /// <summary>実際にコールバックを要求した回数。</summary>
        public int CallbackCount { get; private set; }
        /// <summary>所有者から破棄されたか。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>コールバックを保持するが、開始時には合成しない。</summary>
        public void Start(int sampleRate, int bufferFrames, AudioCallback callback)
        {
            this.callback = callback;
            StartCount++;
        }

        /// <summary>音声デバイスに相当する明示的なバッファ要求。</summary>
        public int RequestFrames(int frames)
        {
            AudioCallback activeCallback = callback ?? throw new InvalidOperationException("出力は停止中です。");
            CallbackCount++;
            return activeCallback(new float[frames * StereoChannels]);
        }

        /// <summary>次のバッファを要求し、実際に合成された PCM を返す。</summary>
        public float[] RequestSamples(int frames)
        {
            AudioCallback activeCallback = callback ?? throw new InvalidOperationException("出力は停止中です。");
            float[] samples = new float[frames * StereoChannels];
            CallbackCount++;
            activeCallback(samples);
            return samples;
        }

        /// <summary>以降のコールバック要求を止める。</summary>
        public void Stop()
        {
            if (callback != null)
            {
                StopCount++;
                callback = null;
            }
        }

        /// <summary>出力を停止し、破棄を記録する。</summary>
        public void Dispose()
        {
            Stop();
            IsDisposed = true;
        }
    }
}
