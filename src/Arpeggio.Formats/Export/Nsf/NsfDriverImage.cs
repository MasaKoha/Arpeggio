using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>固定 bank の生成コード・ラベル・受理済みデータに対する静的 CPU 上限。</summary>
    public sealed class NsfDriverImage
    {
        internal NsfDriverImage(byte[] bank, int codeLength, IReadOnlyDictionary<string, ushort> labels,
            List<NsfDriverInstruction> instructions, long maximumInitCycles, long maximumPlayCycles)
        {
            Bank = Array.AsReadOnly(bank);
            CodeLength = codeLength;
            Labels = new ReadOnlyDictionary<string, ushort>(new Dictionary<string, ushort>(labels));
            Instructions = Array.AsReadOnly(instructions.ToArray());
            MaximumInitCycles = maximumInitCycles;
            MaximumPlayCycles = maximumPlayCycles;
        }

        /// <summary>末尾ゼロ埋めを含む 4096 byte の bank 0。</summary>
        public IReadOnlyList<byte> Bank { get; }
        /// <summary>許可レジスタ表を含む、ゼロ埋め前の使用バイト数。</summary>
        public int CodeLength { get; }
        /// <summary>INIT の実アドレス。</summary>
        public ushort InitAddress => Labels["Init"];
        /// <summary>PLAY の実アドレス。</summary>
        public ushort PlayAddress => Labels["Play"];
        /// <summary>固定 bank 内の全ラベルの実アドレス。</summary>
        public IReadOnlyDictionary<string, ushort> Labels { get; }
        /// <summary>解決済みの命令列。末尾の許可レジスタ表とゼロ埋めは命令に含めない。</summary>
        public IReadOnlyList<NsfDriverInstruction> Instructions { get; }
        /// <summary>再 INIT を含む一回の INIT の静的サイクル上限。</summary>
        public long MaximumInitCycles { get; }
        /// <summary>待機減算・最悪分岐・bank 越えを含む一回の PLAY の静的サイクル上限。</summary>
        public long MaximumPlayCycles { get; }
    }
}
