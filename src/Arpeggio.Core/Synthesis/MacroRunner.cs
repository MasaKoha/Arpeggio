using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>発音からのフレームに対応するマクロ値を保持する。</summary>
    public sealed class MacroRunner
    {
        private Macro? _macro;
        private long _frame;
        private int _fallback;

        /// <summary>現在フレームの値。</summary>
        public int Value => GetValue(_macro, _frame, _fallback);

        /// <summary>新しい発音の先頭へ戻す。</summary>
        public void Reset(Macro? macro, int fallback = 0)
        {
            _macro = macro;
            _fallback = fallback;
            _frame = 0;
        }

        /// <summary>確保せず次のフレームへ進む。</summary>
        public void AdvanceFrame()
        {
            // perf: フレーム数だけを保持し、展開した値列を作らない。
            _frame++;
        }

        /// <summary>終端保持とループを含む指定フレームの値を求める。</summary>
        public static int GetValue(Macro? macro, long frame, int fallback = 0)
        {
            if (macro is null || macro.Values.Length == 0)
            {
                return fallback;
            }
            int length = macro.Values.Length;
            long index = System.Math.Max(0, frame);
            if (index >= length)
            {
                index = macro.LoopIndex < 0
                    ? length - 1
                    : macro.LoopIndex + (index - length) % (length - macro.LoopIndex);
            }
            return macro.Values[(int)index];
        }
    }
}
