using System;

namespace Arpeggio.Daw.Audio
{
    /// <summary>事前確保された左右交互の PCM 領域を埋め、有効なステレオフレーム数を返す。</summary>
    public delegate int AudioCallback(Span<float> interleavedStereo);
}
