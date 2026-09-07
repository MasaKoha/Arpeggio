namespace Arpeggio.Core.Document
{
    /// <summary>NoteEffectKind の種別。</summary>
    public enum NoteEffectKind
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>半音スライド。</summary>
        PitchSlide = 1,
        /// <summary>ビブラート。</summary>
        Vibrato = 2,
        /// <summary>音量スライド。</summary>
        VolumeSlide = 3,
        /// <summary>アルペジオ。</summary>
        Arpeggio = 4,
        /// <summary>発音遅延。</summary>
        Delay = 5,
    }
}
