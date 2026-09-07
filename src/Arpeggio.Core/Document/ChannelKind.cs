namespace Arpeggio.Core.Document
{
    /// <summary>ChannelKind の種別。</summary>
    public enum ChannelKind
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>矩形波。</summary>
        Pulse = 1,
        /// <summary>三角波。</summary>
        Triangle = 2,
        /// <summary>ノイズ。</summary>
        Noise = 3,
        /// <summary>デルタ変調。</summary>
        Dpcm = 4,
        /// <summary>波形メモリ。</summary>
        Wave = 5,
        /// <summary>サンプル。</summary>
        Sample = 6,
    }
}
