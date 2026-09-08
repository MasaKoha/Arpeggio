namespace Arpeggio.Formats.Export
{
    /// <summary>生成ドライバーが使用する文書化済み 6502 opcode の限定集合。</summary>
    public enum NsfOpcode : byte
    {
        /// <summary>命令なし。BRK としては生成しない。</summary>
        None = 0,
        /// <summary>キャリーを解除する。</summary>
        ClearCarry = 0x18,
        /// <summary>絶対アドレスのサブルーチンを呼ぶ。</summary>
        JumpSubroutineAbsolute = 0x20,
        /// <summary>ゼロページ値との論理和。</summary>
        OrAccumulatorZeroPage = 0x05,
        /// <summary>キャリーを設定する。</summary>
        SetCarry = 0x38,
        /// <summary>絶対アドレスへ分岐する。</summary>
        JumpAbsolute = 0x4C,
        /// <summary>呼び出し元へ復帰する。</summary>
        ReturnSubroutine = 0x60,
        /// <summary>ゼロページへ格納する。</summary>
        StoreAccumulatorZeroPage = 0x85,
        /// <summary>絶対アドレスへ格納する。</summary>
        StoreAccumulatorAbsolute = 0x8D,
        /// <summary>キャリー解除時の相対分岐。</summary>
        BranchCarryClear = 0x90,
        /// <summary>X を加算した絶対アドレスへ格納する。</summary>
        StoreAccumulatorAbsoluteX = 0x9D,
        /// <summary>Y に即値をロードする。</summary>
        LoadYImmediate = 0xA0,
        /// <summary>ゼロページからロードする。</summary>
        LoadAccumulatorZeroPage = 0xA5,
        /// <summary>即値をロードする。</summary>
        LoadAccumulatorImmediate = 0xA9,
        /// <summary>アキュムレーターを X へ転送する。</summary>
        TransferAccumulatorToX = 0xAA,
        /// <summary>キャリー設定時の相対分岐。</summary>
        BranchCarrySet = 0xB0,
        /// <summary>ゼロページ間接アドレスに Y を加算してロードする。</summary>
        LoadAccumulatorIndirectY = 0xB1,
        /// <summary>X を加算した絶対アドレスからロードする。</summary>
        LoadAccumulatorAbsoluteX = 0xBD,
        /// <summary>ゼロページの値を減らす。</summary>
        DecrementZeroPage = 0xC6,
        /// <summary>アキュムレーターを即値と比較する。</summary>
        CompareAccumulatorImmediate = 0xC9,
        /// <summary>非ゼロ時の相対分岐。</summary>
        BranchNotEqual = 0xD0,
        /// <summary>ゼロページの値を増やす。</summary>
        IncrementZeroPage = 0xE6,
        /// <summary>ゼロ時の相対分岐。</summary>
        BranchEqual = 0xF0
    }
}
