using System;
using System.Globalization;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Daw.Presenters.Instrument
{
    /// <summary>CLI と同じ整数列とループ位置を音色入力へ変換する。</summary>
    internal static class InstrumentMacroText
    {
        private const int NoLoop = -1;

        internal static Macro? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "null")
            {
                return null;
            }
            string[] sections = text.Split('/');
            if (sections.Length > 2)
            {
                throw new ArgumentException("マクロは値1,値2,.../ループ位置で指定してください。");
            }
            int[] values = ParseValues(sections[0]);
            int loopIndex = sections.Length == 2 ? ParseInteger(sections[1]) : NoLoop;
            if (loopIndex < NoLoop || loopIndex >= values.Length)
            {
                throw new ArgumentException("マクロのループ位置は -1 または値列内の 0 始まり index です。");
            }
            return new Macro { Values = values, LoopIndex = loopIndex };
        }

        internal static int[] ParseValues(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<int>();
            }
            string[] tokens = text.Split(',');
            int[] values = new int[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
            {
                values[index] = ParseInteger(tokens[index]);
            }
            return values;
        }

        private static int ParseInteger(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new ArgumentException($"マクロの値とループ位置は整数です: {text}");
            }
            return value;
        }
    }
}
