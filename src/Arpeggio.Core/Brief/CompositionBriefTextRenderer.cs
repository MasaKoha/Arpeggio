using System.Globalization;
using System.Text;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Brief
{
    /// <summary>作曲指示書を AI へそのまま渡せる日本語テキストへ整形する。</summary>
    public static class CompositionBriefTextRenderer
    {
        private const string Unspecified = "指定なし（AIに一任）";

        /// <summary>未指定のチップとテンポを明示し、空の自由記述は見出しごと省略する。</summary>
        public static string Render(CompositionBrief brief)
        {
            CompositionBriefValidator.Validate(brief);
            var text = new StringBuilder();
            text.Append("# 作曲指示書: ").Append(brief.Title).Append("\n\n");
            text.Append("チップ: ").Append(ChipName(brief.Chip)).Append('\n');
            string tempo = brief.TempoBpm.HasValue
                ? brief.TempoBpm.Value.ToString(CultureInfo.InvariantCulture) + " BPM" : Unspecified;
            text.Append("テンポ目安: ").Append(tempo).Append('\n');
            AppendSection(text, "雰囲気", brief.Mood);
            AppendSection(text, "構成", brief.Structure);
            AppendSection(text, "声の役割", brief.Instrumentation);
            AppendSection(text, "参考", brief.References);
            AppendSection(text, "制約", brief.Constraints);
            AppendSection(text, "メモ", brief.Notes);
            return text.ToString();
        }

        private static string ChipName(ChipKind? chip)
        {
            return chip switch
            {
                ChipKind.Nes => "NES",
                ChipKind.GameBoy => "Game Boy",
                ChipKind.Snes => "SNES",
                _ => Unspecified
            };
        }

        private static void AppendSection(StringBuilder text, string heading, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            text.Append("\n## ").Append(heading).Append('\n').Append(value).Append('\n');
        }
    }
}
