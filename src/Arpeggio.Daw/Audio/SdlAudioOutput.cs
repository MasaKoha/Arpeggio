using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using SDL3;

namespace Arpeggio.Daw.Audio
{
    /// <summary>SDL3 の音声ストリームから固定サイズの PCM バッファを要求する。</summary>
    public sealed class SdlAudioOutput : IAudioOutput
    {
        private const int StereoChannels = 2;
        private const int BytesPerFrame = StereoChannels * sizeof(float);
        private const string BufferFramesHint = "SDL_AUDIO_DEVICE_SAMPLE_FRAMES";
        private readonly SDL.AudioStreamCallback streamCallback;
        private AudioCallback audioCallback = Silence;
        private float[] samples = Array.Empty<float>();
        private GCHandle pinnedSamples;
        private IntPtr stream;
        private Exception? failure;
        private bool isInitialized;
        private bool isDisposed;

        /// <summary>ネイティブ側から呼ばれるデリゲートの寿命を出力先に固定する。</summary>
        public SdlAudioOutput()
        {
            streamCallback = SupplyAudio;
        }

        /// <summary>ネイティブ境界へ伝播させず捕捉した障害を返す。</summary>
        public Exception? Failure => Volatile.Read(ref failure);

        /// <summary>既定の再生デバイスを開き、音声スレッドでの要求を開始する。</summary>
        public void Start(int sampleRate, int bufferFrames, AudioCallback callback)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferFrames);
            ArgumentNullException.ThrowIfNull(callback);
            Stop();
            Volatile.Write(ref failure, null);
            EnsureInitialized(bufferFrames);
            audioCallback = callback;
            samples = new float[checked(bufferFrames * StereoChannels)];
            pinnedSamples = GCHandle.Alloc(samples, GCHandleType.Pinned);
            try
            {
                OpenStream(sampleRate);
            }
            catch
            {
                Stop();
                throw;
            }
        }

        /// <summary>ストリームを閉じてコールバック終了を待ち、固定バッファを解放する。</summary>
        public void Stop()
        {
            if (stream != IntPtr.Zero)
            {
                // Setter がストリームのロックを取得するため、実行中 callback の終了を待ってから解除できる。
                SDL.SetAudioStreamGetCallback(stream, null, IntPtr.Zero);
                SDL.DestroyAudioStream(stream);
                stream = IntPtr.Zero;
            }
            if (pinnedSamples.IsAllocated)
            {
                pinnedSamples.Free();
            }
            audioCallback = Silence;
            samples = Array.Empty<float>();
        }

        /// <summary>再生を終了し、この出力が取得した SDL 音声サブシステムを解放する。</summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            Stop();
            if (isInitialized)
            {
                SDL.QuitSubSystem(SDL.InitFlags.Audio);
                isInitialized = false;
            }
            isDisposed = true;
        }

        private void EnsureInitialized(int bufferFrames)
        {
            SDL.SetHint(BufferFramesHint, bufferFrames.ToString(CultureInfo.InvariantCulture));
            if (isInitialized)
            {
                return;
            }
            if (!SDL.InitSubSystem(SDL.InitFlags.Audio))
            {
                throw new InvalidOperationException($"音声デバイスを初期化できません: {SDL.GetError()}");
            }
            isInitialized = true;
        }

        private void OpenStream(int sampleRate)
        {
            SDL.AudioSpec specification = new SDL.AudioSpec
            {
                Format = BitConverter.IsLittleEndian ? SDL.AudioFormat.AudioF32LE : SDL.AudioFormat.AudioF32BE,
                Channels = StereoChannels,
                Freq = sampleRate
            };
            stream = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in specification, streamCallback, IntPtr.Zero);
            if (stream == IntPtr.Zero)
            {
                throw new InvalidOperationException($"音声ストリームを開けません: {SDL.GetError()}");
            }
            if (!SDL.ResumeAudioStreamDevice(stream))
            {
                throw new InvalidOperationException($"音声再生を開始できません: {SDL.GetError()}");
            }
        }

        private void SupplyAudio(IntPtr userData, IntPtr audioStream, int additionalAmount, int totalAmount)
        {
            if (Volatile.Read(ref failure) != null || additionalAmount <= 0)
            {
                return;
            }
            try
            {
                SupplyRequestedFrames(audioStream, additionalAmount);
            }
            catch (Exception exception)
            {
                // 管理例外が SDL のネイティブスレッドへ抜けるとプロセスが終了するため UI へ引き渡す。
                Volatile.Write(ref failure, exception);
            }
        }

        private void SupplyRequestedFrames(IntPtr audioStream, int requestedBytes)
        {
            int remainingFrames = requestedBytes / BytesPerFrame;
            if (requestedBytes % BytesPerFrame != 0)
            {
                remainingFrames++;
            }
            while (remainingFrames > 0)
            {
                int requestedFrames = Math.Min(remainingFrames, samples.Length / StereoChannels);
                Span<float> buffer = samples.AsSpan(0, requestedFrames * StereoChannels);
                int writtenFrames = audioCallback(buffer);
                if (writtenFrames < 0 || writtenFrames > requestedFrames)
                {
                    throw new InvalidOperationException("音声コールバックが範囲外のフレーム数を返しました。");
                }
                buffer.Slice(writtenFrames * StereoChannels).Clear();
                if (!SDL.PutAudioStreamData(audioStream, pinnedSamples.AddrOfPinnedObject(), requestedFrames * BytesPerFrame))
                {
                    throw new InvalidOperationException($"音声データを送信できません: {SDL.GetError()}");
                }
                remainingFrames -= requestedFrames;
            }
        }

        private static int Silence(Span<float> buffer)
        {
            buffer.Clear();
            return 0;
        }
    }
}
