namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>PLAY フレーム単位のレジスタ書き込み。列内の順序が実行順となる。</summary>
    public readonly record struct NsfRegisterWrite
    {
        /// <summary>絶対 PLAY 番号・実機アドレス・値を指定する。</summary>
        public NsfRegisterWrite(long frame, ushort address, byte value)
        {
            Frame = frame;
            Address = address;
            Value = value;
        }

        /// <summary>最初の PLAY を 0 とする絶対フレーム番号。</summary>
        public long Frame { get; }
        /// <summary>NES の実機レジスタアドレス。</summary>
        public ushort Address { get; }
        /// <summary>書き込む 8 bit 値。</summary>
        public byte Value { get; }
    }
}
