using System;

namespace Arpeggio.Daw.Audio
{
    /// <summary>オーディオスレッドで PCM を取得する出力先を抽象化する。</summary>
    public interface IAudioOutput : IDisposable
    {
        /// <summary>コールバック境界で捕捉した障害。UI スレッドで確認する。</summary>
        Exception? Failure => null;

        /// <summary>指定サイズのバッファを準備し、専用スレッドから取得を開始する。</summary>
        void Start(int sampleRate, int bufferFrames, AudioCallback callback);

        /// <summary>出力を止め、実行中のコールバックが終了するまで待つ。</summary>
        void Stop();
    }
}
