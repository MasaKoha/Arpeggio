namespace Arpeggio.Core.Analysis
{
    /// <summary>音響指標から検出した修正候補。</summary>
    public readonly record struct AnalysisWarning
    {
        /// <summary>警告の種類。</summary>
        public AnalysisWarningKind Kind { get; init; }
        /// <summary>人と AI が読める警告文。</summary>
        public string Message { get; init; }
    }
}
