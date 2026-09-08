using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Export
{
    /// <summary>書き込み順と有限終端を固定した不変のレジスタ列。</summary>
    public sealed class RegisterTimeline
    {
        internal RegisterTimeline(ChipKind chip, long endSamples, List<RegisterWrite> writes)
        {
            Chip = chip;
            EndSamples = endSamples;
            Writes = Array.AsReadOnly(writes.ToArray());
        }

        /// <summary>対象チップ。</summary>
        public ChipKind Chip { get; }
        /// <summary>有限演奏の終端サンプル位置。末尾余白は含めない。</summary>
        public long EndSamples { get; }
        /// <summary>時刻・実行順で整列済みの書き込み。同値でも副作用がある書き込みを保持する。</summary>
        public IReadOnlyList<RegisterWrite> Writes { get; }
    }
}
