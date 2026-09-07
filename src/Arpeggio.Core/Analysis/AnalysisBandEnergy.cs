namespace Arpeggio.Core.Analysis
{
    /// <summary>全窓の片側パワーを集計した帯域比率。</summary>
    public readonly record struct AnalysisBandEnergy
    {
        /// <summary>200 Hz 未満の比率。</summary>
        public double LowRatio { get; init; }
        /// <summary>200〜2000 Hz の比率。</summary>
        public double MidRatio { get; init; }
        /// <summary>2000 Hz 超の比率。</summary>
        public double HighRatio { get; init; }
    }
}
