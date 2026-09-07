using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis
{
    /// <summary>マクロの開始値、保持とループ境界を検証する。</summary>
    public sealed class MacroRunnerTests
    {
        /// <summary>ループなしでは終端値を任意の長時間保持する。</summary>
        [Fact]
        public void GetValue_WithoutLoopHoldsFinalValue()
        {
            var macro = new Macro { Values = new int[] { 15, 12, 7 }, LoopIndex = -1 };

            Assert.Equal(15, MacroRunner.GetValue(macro, 0));
            Assert.Equal(12, MacroRunner.GetValue(macro, 1));
            Assert.Equal(7, MacroRunner.GetValue(macro, 2));
            Assert.Equal(7, MacroRunner.GetValue(macro, long.MaxValue));
        }

        /// <summary>ループ区間だけを繰り返し、非ループの先頭へ戻らない。</summary>
        [Fact]
        public void AdvanceFrame_RepeatsFromLoopIndex()
        {
            var macro = new Macro { Values = new int[] { 15, 12, 7 }, LoopIndex = 1 };
            var runner = new MacroRunner();
            runner.Reset(macro);
            int[] expected = new int[] { 15, 12, 7, 12, 7, 12, 7 };
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index], runner.Value);
                Assert.Equal(expected[index], MacroRunner.GetValue(macro, index));
                runner.AdvanceFrame();
            }
        }

        /// <summary>未設定マクロには明示した既定値を使い、再発音で初期値へ戻る。</summary>
        [Fact]
        public void Reset_RestoresFirstValueOrFallback()
        {
            const int Fallback = 9;
            var macro = new Macro { Values = new int[] { 3, 4 }, LoopIndex = -1 };
            var runner = new MacroRunner();
            runner.Reset(macro);
            runner.AdvanceFrame();
            Assert.Equal(4, runner.Value);
            runner.Reset(macro);
            Assert.Equal(3, runner.Value);
            runner.Reset(null, Fallback);
            runner.AdvanceFrame();
            Assert.Equal(Fallback, runner.Value);
            Assert.Equal(Fallback, MacroRunner.GetValue(null, 100, Fallback));
        }
    }
}
