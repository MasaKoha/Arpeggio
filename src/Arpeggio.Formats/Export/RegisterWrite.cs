namespace Arpeggio.Formats.Export
{
    /// <summary>時刻と副作用の実行順を保持する不変のレジスタ書き込み。</summary>
    public readonly record struct RegisterWrite
    {
        internal RegisterWrite(long positionSamples, int order, ushort address, byte value)
        {
            PositionSamples = positionSamples;
            Order = order;
            Address = address;
            Value = value;
        }

        /// <summary>44100 Hz の絶対サンプル位置。</summary>
        public long PositionSamples { get; }
        /// <summary>書き込み列全体での 0 始まりの実行順。</summary>
        public int Order { get; }
        /// <summary>チップの実機レジスタアドレス。</summary>
        public ushort Address { get; }
        /// <summary>書き込む 8 bit 値。</summary>
        public byte Value { get; }
    }
}
