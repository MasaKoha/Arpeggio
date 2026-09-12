using System;
using System.Collections.Generic;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>容量検証済みの不変な NSF データ列と配置見積もり。</summary>
    public sealed class NsfEncodedData
    {
        internal NsfEncodedData(byte[] bytes, long endFrame, long outputBytes, int maximumWritesPerPlay)
        {
            Bytes = Array.AsReadOnly(bytes);
            EndFrame = endFrame;
            OutputBytes = outputBytes;
            MaximumWritesPerPlay = maximumWritesPerPlay;
        }

        /// <summary>WRITE・WAIT・END のバイト列。bank パディングは含めない。</summary>
        public IReadOnlyList<byte> Bytes { get; }
        /// <summary>有限演奏の最終 PLAY 番号。</summary>
        public long EndFrame { get; }
        /// <summary>ヘッダー・固定 bank・データ bank のゼロ埋めを含む予定ファイルサイズ。</summary>
        public long OutputBytes { get; }
        /// <summary>一回の PLAY に含まれる最大レジスタ書き込み数。</summary>
        public int MaximumWritesPerPlay { get; }
    }
}
