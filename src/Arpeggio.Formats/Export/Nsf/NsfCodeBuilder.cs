using System;
using System.Collections.Generic;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>NSF 専用の限定命令列について、ラベルを解決して固定 bank に収める。</summary>
    internal sealed class NsfCodeBuilder
    {
        private readonly List<byte> _bytes = new List<byte>();
        private readonly List<NsfDriverInstruction> _instructions = new List<NsfDriverInstruction>();
        private readonly Dictionary<string, ushort> _labels = new Dictionary<string, ushort>(StringComparer.Ordinal);
        private int _branchNumber;

        internal IReadOnlyDictionary<string, ushort> Labels => _labels;
        internal int CodeLength => _bytes.Count;

        internal void Label(string name)
            => _labels.Add(name, checked((ushort)(NsfDataFormat.CodeAddress + _bytes.Count)));

        internal void Emit(NsfOpcode opcode, ushort operand = 0, string? targetLabel = null)
        {
            var instruction = new NsfDriverInstruction(checked((ushort)(NsfDataFormat.CodeAddress + _bytes.Count)), opcode, operand, targetLabel);
            _instructions.Add(instruction);
            _bytes.Add((byte)opcode);
            for (int operandIndex = 1; operandIndex < instruction.Length; operandIndex++)
            {
                _bytes.Add((byte)(operand >> ((operandIndex - 1) * NsfDataFormat.ByteShift)));
            }
        }

        internal void Branch(NsfOpcode opcode, string targetLabel)
            => Emit(opcode, targetLabel: targetLabel);

        internal void LongBranch(NsfOpcode opcode, string targetLabel)
        {
            string continuation = "BranchContinuation" + _branchNumber++;
            Branch(NsfInstructionSet.InvertBranch(opcode), continuation);
            Emit(NsfOpcode.JumpAbsolute, targetLabel: targetLabel);
            Label(continuation);
        }

        internal void AppendRegisterTable(string label)
        {
            Label(label);
            for (int offset = 0; offset < NsfDataFormat.RegisterOffsetCount; offset++)
            {
                _bytes.Add(NsfDataFormat.IsAllowedAddress((ushort)(NsfDataFormat.RegisterBase + offset)) ? (byte)1 : (byte)0);
            }
        }

        internal (byte[] Bank, List<NsfDriverInstruction> Instructions) Resolve()
        {
            if (_bytes.Count > ConversionLimits.NsfPlayerBytes)
            {
                throw new InvalidOperationException("NSF ドライバーが固定 bank を超えました。");
            }
            var bank = new byte[ConversionLimits.NsfPlayerBytes];
            _bytes.CopyTo(bank);
            var resolved = new List<NsfDriverInstruction>(_instructions.Count);
            foreach (NsfDriverInstruction instruction in _instructions)
            {
                ushort operand = ResolveOperand(instruction);
                var result = new NsfDriverInstruction(instruction.Address, instruction.Opcode, operand, instruction.TargetLabel);
                resolved.Add(result);
                int offset = instruction.Address - NsfDataFormat.CodeAddress;
                for (int operandIndex = 1; operandIndex < instruction.Length; operandIndex++)
                {
                    bank[offset + operandIndex] = (byte)(operand >> ((operandIndex - 1) * NsfDataFormat.ByteShift));
                }
            }
            return (bank, resolved);
        }

        private ushort ResolveOperand(NsfDriverInstruction instruction)
        {
            if (instruction.TargetLabel is not string label)
            {
                return instruction.Operand;
            }
            if (!_labels.TryGetValue(label, out ushort address))
            {
                throw new InvalidOperationException("未定義の NSF ラベルです: " + label);
            }
            if (!NsfInstructionSet.IsBranch(instruction.Opcode))
            {
                return address;
            }
            int displacement = address - instruction.Address - instruction.Length;
            if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue)
            {
                throw new InvalidOperationException("NSF の相対分岐が範囲外です: " + label);
            }
            return unchecked((byte)(sbyte)displacement);
        }
    }
}
