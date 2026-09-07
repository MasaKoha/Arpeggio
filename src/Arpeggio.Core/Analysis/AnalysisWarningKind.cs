namespace Arpeggio.Core.Analysis
{
    /// <summary>音声を修正するための警告分類。</summary>
    public enum AnalysisWarningKind
    {
        /// <summary>警告なし。</summary>
        None = 0,
        /// <summary>フルスケール以上のサンプルがある。</summary>
        Clipping = 1,
        /// <summary>全体音量が小さすぎる。</summary>
        TooQuiet = 2,
        /// <summary>先頭 50 ms が無音。</summary>
        LeadingSilence = 3,
        /// <summary>末尾に 1 秒以上の無音。</summary>
        TrailingSilence = 4,
        /// <summary>左右の RMS 差が大きい。</summary>
        StereoImbalance = 5
    }
}
