namespace Arpeggio.Core.Render
{
    /// <summary>補正箇所と要求値・適用値を持つ確保不要の警告。</summary>
    public readonly record struct RenderWarning
    {
        /// <summary>トラックと tick で編集対象を特定する。</summary>
        public RenderWarning(RenderWarningKind kind, int trackIndex, int tick, double requestedValue, double actualValue)
        {
            Kind = kind;
            TrackIndex = trackIndex;
            Tick = tick;
            RequestedValue = requestedValue;
            ActualValue = actualValue;
        }
        /// <summary>補正の理由。</summary>
        public RenderWarningKind Kind { get; init; }
        /// <summary>ソング内のトラック位置。</summary>
        public int TrackIndex { get; init; }
        /// <summary>元のノート開始 tick。</summary>
        public int Tick { get; init; }
        /// <summary>編集データで指定された値。</summary>
        public double RequestedValue { get; init; }
        /// <summary>合成で採用した値。</summary>
        public double ActualValue { get; init; }
    }
}
