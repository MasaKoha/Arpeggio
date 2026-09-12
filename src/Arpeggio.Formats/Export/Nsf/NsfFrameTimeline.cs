using System;
using System.Collections.Generic;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>PLAY 単位の有限終端と書き込み順を保持する不変の列。</summary>
    public sealed class NsfFrameTimeline
    {
        /// <summary>入力列をコピーする。時刻・アドレス・容量の適合は符号化時に診断する。</summary>
        public NsfFrameTimeline(long endFrame, IReadOnlyList<NsfRegisterWrite> writes)
        {
            ArgumentNullException.ThrowIfNull(writes);
            if (writes.Count > ConversionLimits.MaximumRegisterWrites)
            {
                throw new ArgumentOutOfRangeException(nameof(writes));
            }
            EndFrame = endFrame;
            var copy = new NsfRegisterWrite[writes.Count];
            for (int writeIndex = 0; writeIndex < copy.Length; writeIndex++)
            {
                copy[writeIndex] = writes[writeIndex];
            }
            Writes = Array.AsReadOnly(copy);
        }

        /// <summary>全停止を書き込む絶対 PLAY 番号。</summary>
        public long EndFrame { get; }
        /// <summary>時刻順・副作用の実行順を保持した書き込み列。</summary>
        public IReadOnlyList<NsfRegisterWrite> Writes { get; }
    }
}
