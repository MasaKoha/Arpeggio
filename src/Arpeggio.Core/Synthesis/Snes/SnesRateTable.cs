using System;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>ADSR とノイズの進行に共通する DSP サンプル周期。</summary>
    public static class SnesRateTable
    {
        /// <summary>DSP の固定動作レート。</summary>
        public const int SampleRate = 32000;
        /// <summary>最大の速度レジスタ。</summary>
        public const int MaximumRate = 31;
        private static readonly int[] Periods =
        {
            0, 2048, 1536, 1280, 1024, 768, 640, 512,
            384, 320, 256, 192, 160, 128, 96, 80,
            64, 48, 40, 32, 24, 20, 16, 12,
            10, 8, 6, 5, 4, 3, 2, 1
        };

        /// <summary>更新間隔を返す。レート 0 の戻り値 0 は停止を表す。</summary>
        public static int GetPeriod(int rate)
        {
            if ((uint)rate > MaximumRate)
            {
                throw new ArgumentOutOfRangeException(nameof(rate));
            }
            return Periods[rate];
        }
    }
}
