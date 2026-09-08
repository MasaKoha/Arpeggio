namespace Arpeggio.Formats.Export
{
    /// <summary>ラベル解決後の一命令。機械語とサイクル見積もりをレビューするための情報。</summary>
    public sealed record NsfDriverInstruction
    {
        internal NsfDriverInstruction(ushort address, NsfOpcode opcode, ushort operand, string? targetLabel)
        {
            Address = address;
            Opcode = opcode;
            Operand = operand;
            TargetLabel = targetLabel;
            (Length, MaximumCycles) = NsfInstructionSet.Describe(opcode);
        }

        /// <summary>固定 bank 内の実行アドレス。</summary>
        public ushort Address { get; }
        /// <summary>文書化された限定 opcode。</summary>
        public NsfOpcode Opcode { get; }
        /// <summary>解決済みオペランド。相対分岐では下位 byte を符号付き変位として読む。</summary>
        public ushort Operand { get; }
        /// <summary>命令バイト数。</summary>
        public int Length { get; }
        /// <summary>分岐・ページ越えを含む、この命令単独のサイクル上限。</summary>
        public int MaximumCycles { get; }
        /// <summary>解決した参照先ラベル。数値指定の命令では null。</summary>
        public string? TargetLabel { get; }
    }
}
