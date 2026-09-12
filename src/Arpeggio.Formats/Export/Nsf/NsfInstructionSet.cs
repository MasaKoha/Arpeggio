using System;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>生成で使用する命令の長さと最悪サイクル数だけを定義する。</summary>
    internal static class NsfInstructionSet
    {
        internal static (int Length, int Cycles) Describe(NsfOpcode opcode)
            => opcode switch
            {
                NsfOpcode.ClearCarry or NsfOpcode.SetCarry or NsfOpcode.TransferAccumulatorToX => (1, 2),
                NsfOpcode.ReturnSubroutine => (1, 6),
                NsfOpcode.LoadAccumulatorImmediate or NsfOpcode.LoadYImmediate or NsfOpcode.CompareAccumulatorImmediate => (2, 2),
                NsfOpcode.LoadAccumulatorZeroPage or NsfOpcode.StoreAccumulatorZeroPage or NsfOpcode.OrAccumulatorZeroPage => (2, 3),
                NsfOpcode.IncrementZeroPage or NsfOpcode.DecrementZeroPage => (2, 5),
                NsfOpcode.BranchEqual or NsfOpcode.BranchNotEqual or NsfOpcode.BranchCarrySet or NsfOpcode.BranchCarryClear => (2, 4),
                NsfOpcode.LoadAccumulatorIndirectY => (2, 6),
                NsfOpcode.StoreAccumulatorAbsolute => (3, 4),
                NsfOpcode.LoadAccumulatorAbsoluteX or NsfOpcode.StoreAccumulatorAbsoluteX => (3, 5),
                NsfOpcode.JumpAbsolute => (3, 3),
                NsfOpcode.JumpSubroutineAbsolute => (3, 6),
                _ => throw new ArgumentOutOfRangeException(nameof(opcode), "生成対象外の opcode です。")
            };

        internal static bool IsBranch(NsfOpcode opcode)
            => opcode is NsfOpcode.BranchEqual or NsfOpcode.BranchNotEqual or NsfOpcode.BranchCarrySet or NsfOpcode.BranchCarryClear;

        internal static NsfOpcode InvertBranch(NsfOpcode opcode)
            => opcode switch
            {
                NsfOpcode.BranchEqual => NsfOpcode.BranchNotEqual,
                NsfOpcode.BranchNotEqual => NsfOpcode.BranchEqual,
                NsfOpcode.BranchCarrySet => NsfOpcode.BranchCarryClear,
                NsfOpcode.BranchCarryClear => NsfOpcode.BranchCarrySet,
                _ => throw new ArgumentOutOfRangeException(nameof(opcode))
            };
    }
}
