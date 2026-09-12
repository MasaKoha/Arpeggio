using System.Collections.Generic;
using System.Text;
using Arpeggio.Formats;

namespace Arpeggio.Daw.Presenters.Export
{
    /// <summary>チップ変換の長さ・全診断件数・元位置・方式の制限を非モーダル表示用に整形する。</summary>
    internal static class ChipExportReportText
    {
        internal const string InitialLimitations = "通常の再生とは位相・音量・ミキサーが異なります。有限回数を展開して保存し、無限ループには対応しません。";

        internal static string Format(ConversionReport report)
        {
            var text = new StringBuilder();
            text.AppendLine($"{report.Format} / {report.Chip} — {report.DurationSeconds:F6} 秒 / {report.OutputBytes} byte");
            text.AppendLine($"警告 {report.WarningCount} / エラー {report.ErrorCount}");
            if (report.Strict && report.WarningCount > 0)
            {
                text.AppendLine("strict: 変換警告があるため保存できません。");
            }
            foreach (string limitation in report.Limitations)
            {
                text.AppendLine($"制限: {limitation}");
            }
            AppendDiagnostics(text, "エラー", report.Errors);
            AppendDiagnostics(text, "警告", report.Warnings);
            if (report.DroppedWarningCount > 0 || report.DroppedErrorCount > 0)
            {
                text.AppendLine($"明細保持上限超過: 警告 {report.DroppedWarningCount} / エラー {report.DroppedErrorCount}");
            }
            return text.ToString();
        }

        private static void AppendDiagnostics(StringBuilder text, string label, IReadOnlyList<ConversionDiagnostic> diagnostics)
        {
            foreach (ConversionDiagnostic diagnostic in diagnostics)
            {
                text.AppendLine($"{label} {diagnostic.Code}: {diagnostic.Message} " +
                    $"(track={diagnostic.SourceTrack}, channel={diagnostic.SourceChannel}, tick={diagnostic.SourceTick}, event={diagnostic.SourceEvent}, " +
                    $"outputTrack={diagnostic.OutputTrack}, outputTick={diagnostic.OutputTick}, count={diagnostic.OccurrenceCount}) " +
                    $"{diagnostic.Original} → {diagnostic.Converted}");
            }
        }
    }
}
