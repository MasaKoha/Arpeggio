using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Brief
{
    /// <summary>AI に渡す見出し・省略・未指定表示と本文の保持を固定する。</summary>
    public sealed class CompositionBriefTextRendererTests
    {
        /// <summary>未指定のチップとテンポは明示し、空白だけの本文も見出しごと省く。</summary>
        [Fact]
        public void UnspecifiedValuesAreExplicitAndEmptySectionsAreOmitted()
        {
            var brief = new CompositionBrief { Title = "仮題", Mood = "", Structure = " \n", Notes = "\t" };
            Assert.Equal("# 作曲指示書: 仮題\n\nチップ: 指定なし（AIに一任）\nテンポ目安: 指定なし（AIに一任）\n",
                CompositionBriefTextRenderer.Render(brief));
        }

        /// <summary>全項目の順番と複数行・前後空白をそのまま維持する。</summary>
        [Fact]
        public void FullTextUsesSpecifiedOrderAndPreservesBody()
        {
            var brief = new CompositionBrief
            {
                Title = "廃墟の朝", Chip = ChipKind.Nes, TempoBpm = 96,
                Mood = "寂しい\n希望", Structure = "  イントロ\r\nメイン  ",
                Instrumentation = "主旋律 / 低音", References = "参考曲", Constraints = "ループ", Notes = "補足"
            };
            string expected = "# 作曲指示書: 廃墟の朝\n\nチップ: NES\nテンポ目安: 96 BPM\n"
                + "\n## 雰囲気\n寂しい\n希望\n\n## 構成\n  イントロ\r\nメイン  \n"
                + "\n## 声の役割\n主旋律 / 低音\n\n## 参考\n参考曲\n\n## 制約\nループ\n\n## メモ\n補足\n";
            Assert.Equal(expected, CompositionBriefTextRenderer.Render(brief));
        }

        /// <summary>チップとテンポは互いの指定有無に依存しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, "NES")]
        [InlineData(ChipKind.GameBoy, "Game Boy")]
        [InlineData(ChipKind.Snes, "SNES")]
        public void ChipAndTempoCanBeSpecifiedIndependently(ChipKind chip, string label)
        {
            string chipOnly = CompositionBriefTextRenderer.Render(new CompositionBrief { Title = "曲", Chip = chip });
            Assert.Contains("チップ: " + label + "\n", chipOnly);
            Assert.Contains("テンポ目安: 指定なし（AIに一任）", chipOnly);
            string tempoOnly = CompositionBriefTextRenderer.Render(new CompositionBrief { Title = "曲", TempoBpm = 120 });
            Assert.Contains("チップ: 指定なし（AIに一任）", tempoOnly);
            Assert.Contains("テンポ目安: 120 BPM", tempoOnly);
        }
    }
}
