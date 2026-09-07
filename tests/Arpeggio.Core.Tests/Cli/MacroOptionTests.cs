using System;
using Arpeggio.Cli;
using Arpeggio.Core.Instruments;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>マクロ入力の値・単位非依存の符号・ループ境界を検証する。</summary>
    public sealed class MacroOptionTests
    {
        /// <summary>末尾保持と指定フレームへのループを区別する。</summary>
        [Theory]
        [InlineData("15,14,12", -1)]
        [InlineData("15,14,12/2", 2)]
        [InlineData(" 15, 14,12 / 0 ", 0)]
        [InlineData("15,14,12/-1", -1)]
        public void ParsesValuesAndLoop(string text, int loopIndex)
        {
            Macro macro = MacroOption.Parse(text);
            Assert.Equal(new[] { 15, 14, 12 }, macro.Values);
            Assert.Equal(loopIndex, macro.LoopIndex);
        }

        /// <summary>ピッチ用の負値と空列を音量マクロの範囲で制限しない。</summary>
        [Fact]
        public void PreservesSignedValuesAndEmptySequence()
        {
            Assert.Equal(new[] { -100, 0, 100 }, MacroOption.Parse("-100,0,100").Values);
            Assert.Empty(MacroOption.Parse("").Values);
            Assert.Equal(-1, MacroOption.Parse("").LoopIndex);
        }

        /// <summary>欠落した要素・非整数・ループ位置不正は操作エラーになる。</summary>
        [Theory]
        [InlineData("15,,12")]
        [InlineData("15,")]
        [InlineData("15.5")]
        [InlineData("15/1")]
        [InlineData("15/-2")]
        [InlineData("15/")]
        [InlineData("15/0/0")]
        [InlineData("/0")]
        [InlineData("2147483648")]
        [InlineData("null")]
        public void RejectsInvalidInput(string text)
        {
            Assert.Throws<ArgumentException>(() => MacroOption.Parse(text));
        }
    }
}
