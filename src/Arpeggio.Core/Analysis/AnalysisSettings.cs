namespace Arpeggio.Core.Analysis
{
    /// <summary>時系列窓・無音閾値・周波数解析の解像度。</summary>
    public readonly record struct AnalysisSettings
    {
        /// <summary>標準の解析設定を作る。</summary>
        public AnalysisSettings() : this(100, -60, 2048) { }

        /// <summary>ミリ秒・dBFS・基数 2 の変換点数を指定する。</summary>
        public AnalysisSettings(int WindowMilliseconds = 100, double SilenceThresholdDb = -60, int FftSize = 2048)
        {
            this.WindowMilliseconds = WindowMilliseconds;
            this.SilenceThresholdDb = SilenceThresholdDb;
            this.FftSize = FftSize;
        }

        /// <summary>重複しない時系列窓の長さ（ミリ秒）。</summary>
        public int WindowMilliseconds { get; init; }
        /// <summary>この RMS dBFS 未満を無音とする。</summary>
        public double SilenceThresholdDb { get; init; }
        /// <summary>周波数変換の点数。2 以上の 2 の累乗。</summary>
        public int FftSize { get; init; }
    }
}
