using System.Collections.Generic;
using System.Text;
using Arpeggio.Formats;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>MIDI の採用数・損失・実際の声割り当てと共通診断を表示する。</summary>
    internal static class MidiImportReportText
    {
        private const int ChannelPart = 1;
        private const int TrackPart = 3;
        internal const string InitialLimitations = "GM 音色は近似されます。テンポ変化は固定 BPM の位置へ焼き込みます。候補の確認・新規保存では現在の曲と履歴を変更しません。";

        internal static string Format(ConversionReport report)
        {
            var text = new StringBuilder();
            text.AppendLine($"採用 {Count(report, "acceptedMidiNotes")} / 声数不足で破棄 {Count(report, "polyphonyNotesDropped")} / 打ち切り {Count(report, "truncatedMidiNotes")}");
            text.AppendLine($"明示除外 {Count(report, "explicitlyExcludedMidiNotes")} / 音量ゼロ {Count(report, "zeroVolumeNotesDropped")} / 長さゼロ {Count(report, "zeroLengthNotesDropped")}");
            text.AppendLine("実際の MIDI channel → 出力トラック（0 始まり）:");
            foreach (KeyValuePair<string, long> statistic in report.Statistics)
            {
                if (!statistic.Key.StartsWith("midiChannel.", System.StringComparison.Ordinal)) { continue; }
                string[] parts = statistic.Key.Split('.');
                text.AppendLine($"ch {parts[ChannelPart]} → track {parts[TrackPart]}: {statistic.Value} 音");
            }
            text.Append(ChipExportReportText.Format(report));
            return text.ToString();
        }

        private static long Count(ConversionReport report, string key) => report.Statistics.TryGetValue(key, out long count) ? count : 0;
    }
}
