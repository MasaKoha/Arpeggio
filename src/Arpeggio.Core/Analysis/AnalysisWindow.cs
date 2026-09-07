namespace Arpeggio.Core.Analysis
{
    /// <summary>一つの時系列窓の音量と周波数の特徴。</summary>
    public readonly record struct AnalysisWindow
    {
        /// <summary>窓の開始時刻（秒）。</summary>
        public double StartSeconds { get; init; }
        /// <summary>左右全サンプルの RMS（dBFS）。</summary>
        public double RmsDbfs { get; init; }
        /// <summary>左右の絶対値ピーク（dBFS）。</summary>
        public double PeakDbfs { get; init; }
        /// <summary>最大パワーの周波数（Hz）。無音は 0。</summary>
        public double DominantFrequencyHz { get; init; }
        /// <summary>支配的周波数の最寄り音名。無音・MIDI 範囲外は null。</summary>
        public string? NearestNoteName { get; init; }
        /// <summary>片側パワーの周波数加重平均（Hz）。</summary>
        public double SpectralCentroidHz { get; init; }
    }
}
