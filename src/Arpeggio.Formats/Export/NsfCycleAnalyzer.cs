using System;
using System.Collections.Generic;

namespace Arpeggio.Formats.Export
{
    /// <summary>既知のループ境界で区切った生成命令グラフの最長経路を静的に求める。</summary>
    internal sealed class NsfCycleAnalyzer
    {
        private readonly Dictionary<ushort, NsfDriverInstruction> _instructions = new Dictionary<ushort, NsfDriverInstruction>();
        private readonly Dictionary<ushort, long> _memo = new Dictionary<ushort, long>();
        private readonly HashSet<ushort> _active = new HashSet<ushort>();
        private readonly HashSet<ushort> _boundaries = new HashSet<ushort>();

        internal NsfCycleAnalyzer(IReadOnlyList<NsfDriverInstruction> instructions)
        {
            foreach (NsfDriverInstruction instruction in instructions)
            {
                _instructions.Add(instruction.Address, instruction);
            }
        }

        internal long MaximumPath(ushort entry, params ushort[] boundaries)
        {
            _memo.Clear();
            _active.Clear();
            _boundaries.Clear();
            _boundaries.UnionWith(boundaries);
            // 開始命令だけは境界でも評価し、WRITE の末尾から戻ったときに打ち切る。
            return Evaluate(_instructions[entry]);
        }

        private long Visit(ushort address)
        {
            if (_boundaries.Contains(address))
            {
                return 0;
            }
            if (_memo.TryGetValue(address, out long cycles))
            {
                return cycles;
            }
            if (!_active.Add(address))
            {
                throw new InvalidOperationException("静的上限の未定義ループが NSF 命令列にあります。");
            }
            cycles = Evaluate(_instructions[address]);
            _active.Remove(address);
            _memo.Add(address, cycles);
            return cycles;
        }

        private long Evaluate(NsfDriverInstruction instruction)
        {
            long cycles = instruction.MaximumCycles;
            ushort next = checked((ushort)(instruction.Address + instruction.Length));
            if (instruction.Opcode == NsfOpcode.ReturnSubroutine)
            {
                return cycles;
            }
            if (instruction.Opcode == NsfOpcode.JumpAbsolute)
            {
                return checked(cycles + Visit(instruction.Operand));
            }
            if (instruction.Opcode == NsfOpcode.JumpSubroutineAbsolute)
            {
                return checked(cycles + Visit(instruction.Operand) + Visit(next));
            }
            if (NsfInstructionSet.IsBranch(instruction.Opcode))
            {
                ushort target = checked((ushort)(next + unchecked((sbyte)instruction.Operand)));
                return checked(cycles + Math.Max(Visit(next), Visit(target)));
            }
            return checked(cycles + Visit(next));
        }
    }
}
