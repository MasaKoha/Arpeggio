using System;
using System.Text;
using Arpeggio.Core.Render;

namespace Arpeggio.Core.Analysis
{
    /// <summary>解析結果を人と AI 共通の要約・警告・時系列表にする。</summary>
    public static class AnalysisTextRenderer
    {
        private const int MaximumDisplayedWindows = 40;

        /// <summary>先頭 40 窓まで表示する。全件は解析レポートの JSON を使用する。</summary>
        public static string Render(AnalysisReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(FormattableString.Invariant($"長さ: {report.DurationSeconds:F4} s | {report.SampleRate} Hz | 窓: {report.Settings.WindowMilliseconds} ms"));
            text.AppendLine(FormattableString.Invariant($"RMS: {report.RmsDbfs:F2} dBFS | ピーク: {report.PeakDbfs:F2} dBFS | クリップ: {report.ClippedSampleCount}"));
            text.AppendLine(FormattableString.Invariant($"無音割合: {report.SilenceRatio:P2} | 左右差 L-R: {report.LeftRightBalanceDb:F2} dB"));
            text.AppendLine(FormattableString.Invariant($"帯域: 低 <200 Hz {report.BandEnergy.LowRatio:P2} | 中 200–2000 Hz {report.BandEnergy.MidRatio:P2} | 高 >2000 Hz {report.BandEnergy.HighRatio:P2}"));
            text.AppendLine();
            AppendWarnings(text, report);
            text.AppendLine();
            text.AppendLine("開始 s | RMS dBFS | ピーク dBFS | 支配的 Hz | 音名 | 重心 Hz");
            int count = Math.Min(MaximumDisplayedWindows, report.Windows.Count);
            for (int index = 0; index < count; index++)
            {
                AnalysisWindow window = report.Windows[index];
                text.AppendLine(FormattableString.Invariant($"{window.StartSeconds:F4} | {window.RmsDbfs:F2} | {window.PeakDbfs:F2} | {window.DominantFrequencyHz:F2} | {window.NearestNoteName ?? "-"} | {window.SpectralCentroidHz:F2}"));
            }
            if (count < report.Windows.Count)
            {
                text.AppendLine($"残り {report.Windows.Count - count} 窓は省略。--json で全件を取得してください。");
            }
            return text.ToString();
        }

        private static void AppendWarnings(StringBuilder text, AnalysisReport report)
        {
            text.AppendLine("警告:");
            if (report.Warnings.Count == 0 && report.RenderWarnings.Count == 0 && report.DroppedRenderWarningCount == 0)
            {
                text.AppendLine("なし");
            }
            foreach (AnalysisWarning warning in report.Warnings)
            {
                text.AppendLine($"音響 {warning.Kind}: {warning.Message}");
            }
            foreach (RenderWarning warning in report.RenderWarnings)
            {
                text.AppendLine(FormattableString.Invariant($"合成 {warning.Kind}: track {warning.TrackIndex}, tick {warning.Tick}, {warning.RequestedValue} -> {warning.ActualValue}"));
            }
            if (report.DroppedRenderWarningCount > 0)
            {
                text.AppendLine($"合成警告の保持上限超過: {report.DroppedRenderWarningCount}");
            }
        }
    }
}
