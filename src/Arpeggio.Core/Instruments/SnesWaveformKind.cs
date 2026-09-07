namespace Arpeggio.Core.Instruments
{
    /// <summary>SnesWaveformKind の種別。</summary>
    public enum SnesWaveformKind
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>正弦波。</summary>
        Sine = 1,
        /// <summary>矩形波。</summary>
        Square = 2,
        /// <summary>鋸歯状波。</summary>
        Saw = 3,
        /// <summary>三角波。</summary>
        Triangle = 4,
        /// <summary>パルス波。</summary>
        Pulse = 5,
        /// <summary>ノイズ。</summary>
        Noise = 6,
    }
}
