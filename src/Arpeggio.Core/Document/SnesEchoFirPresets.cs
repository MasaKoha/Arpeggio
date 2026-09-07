using System;

namespace Arpeggio.Core.Document
{
    /// <summary>編集可能な独立配列として提供する SNES エコー FIR プリセット。</summary>
    public static class SnesEchoFirPresets
    {
        /// <summary>ほぼフラットな係数。</summary>
        public static int[] Flat => new[] { 127, 0, 0, 0, 0, 0, 0, 0 };
        /// <summary>高域を減らす係数。</summary>
        public static int[] LowPass => new[] { 16, 32, 32, 32, 16, 0, 0, 0 };
        /// <summary>低域を減らす差分係数。</summary>
        public static int[] HighPass => new[] { 64, -64, 0, 0, 0, 0, 0, 0 };
        /// <summary>時間方向へ広がる係数。</summary>
        public static int[] Wide => new[] { 64, 0, 32, 0, 16, 0, 16, 0 };

        /// <summary>プリセット名に対応する係数配列を取得する。</summary>
        public static int[] Get(string name)
        {
            return name switch
            {
                nameof(Flat) => Flat,
                nameof(LowPass) => LowPass,
                nameof(HighPass) => HighPass,
                nameof(Wide) => Wide,
                _ => throw new ArgumentException("未対応の SNES FIR プリセットです。", nameof(name))
            };
        }
    }
}
