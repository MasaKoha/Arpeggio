namespace Arpeggio.Core.Render
{
    /// <summary>例外にせず AI や編集画面へ返す合成上の補正理由。</summary>
    public enum RenderWarningKind
    {
        /// <summary>警告なし。</summary>
        None = 0,
        /// <summary>発音可能音域へのクランプ。</summary>
        PitchClamped = 1,
        /// <summary>NES 三角波の固定音量による指定音量の無視。</summary>
        TriangleVolumeIgnored = 2
    }
}
