namespace Arpeggio.Formats.Export.Control
{
    /// <summary>制御列の作成結果。失敗時に部分列を公開しない。</summary>
    public sealed class ControlTimelineResult
    {
        internal ControlTimelineResult(ControlTimeline? timeline, ConversionReport report)
        {
            Timeline = timeline;
            Report = report;
        }

        /// <summary>成功時の不変な制御列。変換エラー時は null。</summary>
        public ControlTimeline? Timeline { get; }
        /// <summary>共通の診断結果。</summary>
        public ConversionReport Report { get; }
    }
}
