using System;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Export.Nes;

namespace Arpeggio.Formats.Export.Nsf
{
    /// <summary>曲の解釈を行わない自作 6502 プレイヤーを、限定命令とラベルから生成する。</summary>
    public static class NsfDriverBuilder
    {
        private const byte CursorLow = 0x00;
        private const byte CursorHigh = 0x01;
        private const byte DataBank = 0x02;
        private const byte WaitLow = 0x03;
        private const byte WaitHigh = 0x04;
        private const byte Ended = 0x05;
        private const byte WorkRamBytes = 0x20;
        private const byte EndedValue = 1;
        /// <summary>INIT 一回の許容サイクル上限。</summary>
        public const int MaximumInitBudget = 20000;
        /// <summary>PLAY 一回の許容サイクル上限。</summary>
        public const int MaximumPlayBudget = 8000;

        /// <summary>固定 bank と静的上限を生成する。予算超過・先行エラー・strict 警告時は null を返す。</summary>
        public static NsfDriverImage? Build(NsfEncodedData data, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Nsf || report.Chip != ChipKind.Nes)
            {
                throw new ArgumentException("NES NSF のレポートを指定してください。", nameof(report));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var code = new NsfCodeBuilder();
            EmitInit(code);
            EmitPlay(code);
            EmitReadByte(code);
            code.AppendRegisterTable("AllowedRegisters");
            var (bank, instructions) = code.Resolve();
            var analyzer = new NsfCycleAnalyzer(instructions);
            long initCycles = analyzer.MaximumPath(code.Labels["Init"]);
            long entryCycles = analyzer.MaximumPath(code.Labels["Play"], code.Labels["NextCommand"]);
            long writeCycles = analyzer.MaximumPath(code.Labels["NextCommand"], code.Labels["NextCommand"],
                code.Labels["WaitCommand"], code.Labels["EndCommand"]);
            long terminalCycles = analyzer.MaximumPath(code.Labels["NextCommand"], code.Labels["WriteCommand"]);
            long playCycles = checked(entryCycles + data.MaximumWritesPerPlay * writeCycles + terminalCycles);
            report.SetStatistic("maximumInitCycles", initCycles);
            report.SetStatistic("maximumPlayCycles", playCycles);
            report.SetStatistic("nsfPlayEntryCycles", entryCycles);
            report.SetStatistic("nsfWriteCommandCycles", writeCycles);
            report.SetStatistic("nsfTerminalCommandCycles", terminalCycles);
            if (initCycles > MaximumInitBudget || playCycles > MaximumPlayBudget)
            {
                report.AddError(new ConversionDiagnostic("NsfCpuBudgetExceeded", "生成プレイヤーの静的 CPU 上限が INIT 20000／PLAY 8000 cycles を超えています。"));
            }
            return report.CanWrite ? new NsfDriverImage(bank, code.CodeLength, code.Labels, instructions, initCycles, playCycles) : null;
        }

        private static void EmitInit(NsfCodeBuilder code)
        {
            code.Label("Init");
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, 0);
            // 呼び出し元の RAM 初期値へ依存せず、再 INIT でも予約領域全体を同じ状態へ戻す。
            for (byte address = 0; address < WorkRamBytes; address++)
            {
                code.Emit(NsfOpcode.StoreAccumulatorZeroPage, address);
            }
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.Status);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.DmcControl);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.DmcOutput);
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, NesRegisters.SweepDisabled);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.PulseOneSweep);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.PulseTwoSweep);
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, NesRegisters.FiveStepInterruptDisabled);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.FrameCounter);
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, NsfDataFormat.DataAddress >> NsfDataFormat.ByteShift);
            code.Emit(NsfOpcode.StoreAccumulatorZeroPage, CursorHigh);
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, NsfDataFormat.FirstDataBank);
            code.Emit(NsfOpcode.StoreAccumulatorZeroPage, DataBank);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NsfDataFormat.BankRegister);
            code.Emit(NsfOpcode.ReturnSubroutine);
        }

        private static void EmitPlay(NsfCodeBuilder code)
        {
            code.Label("Play");
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, Ended);
            code.LongBranch(NsfOpcode.BranchNotEqual, "Return");
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, WaitLow);
            code.Emit(NsfOpcode.OrAccumulatorZeroPage, WaitHigh);
            code.Branch(NsfOpcode.BranchEqual, "NextCommand");
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, WaitLow);
            code.Branch(NsfOpcode.BranchNotEqual, "DecrementWaitLow");
            code.Emit(NsfOpcode.DecrementZeroPage, WaitHigh);
            code.Label("DecrementWaitLow");
            code.Emit(NsfOpcode.DecrementZeroPage, WaitLow);
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, WaitLow);
            code.Emit(NsfOpcode.OrAccumulatorZeroPage, WaitHigh);
            code.LongBranch(NsfOpcode.BranchNotEqual, "Return");
            EmitDispatch(code);
            EmitWrite(code);
            EmitWait(code);
            EmitStop(code);
        }

        private static void EmitDispatch(NsfCodeBuilder code)
        {
            code.Label("NextCommand");
            ReadOrStop(code);
            code.Emit(NsfOpcode.CompareAccumulatorImmediate, NsfDataFormat.Write);
            code.Branch(NsfOpcode.BranchEqual, "WriteCommand");
            code.Emit(NsfOpcode.CompareAccumulatorImmediate, NsfDataFormat.Wait);
            code.Branch(NsfOpcode.BranchEqual, "WaitCommand");
            code.Emit(NsfOpcode.CompareAccumulatorImmediate, NsfDataFormat.End);
            code.Branch(NsfOpcode.BranchEqual, "EndCommand");
            code.Emit(NsfOpcode.JumpAbsolute, targetLabel: "Fail");
        }

        private static void EmitWrite(NsfCodeBuilder code)
        {
            code.Label("WriteCommand");
            ReadOrStop(code);
            code.Emit(NsfOpcode.CompareAccumulatorImmediate, NsfDataFormat.RegisterOffsetCount);
            code.LongBranch(NsfOpcode.BranchCarrySet, "Fail");
            code.Emit(NsfOpcode.TransferAccumulatorToX);
            code.Emit(NsfOpcode.LoadAccumulatorAbsoluteX, targetLabel: "AllowedRegisters");
            code.LongBranch(NsfOpcode.BranchEqual, "Fail");
            ReadOrStop(code);
            code.Emit(NsfOpcode.StoreAccumulatorAbsoluteX, NsfDataFormat.RegisterBase);
            code.Emit(NsfOpcode.JumpAbsolute, targetLabel: "NextCommand");
        }

        private static void EmitWait(NsfCodeBuilder code)
        {
            code.Label("WaitCommand");
            ReadOrStop(code);
            code.Emit(NsfOpcode.StoreAccumulatorZeroPage, WaitLow);
            ReadOrStop(code);
            code.Emit(NsfOpcode.StoreAccumulatorZeroPage, WaitHigh);
            code.Emit(NsfOpcode.OrAccumulatorZeroPage, WaitLow);
            code.LongBranch(NsfOpcode.BranchEqual, "Fail");
            code.Emit(NsfOpcode.ReturnSubroutine);
        }

        private static void EmitStop(NsfCodeBuilder code)
        {
            code.Label("Fail");
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, 0);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NesRegisters.Status);
            code.Label("EndCommand");
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, EndedValue);
            code.Emit(NsfOpcode.StoreAccumulatorZeroPage, Ended);
            code.Label("Return");
            code.Emit(NsfOpcode.ReturnSubroutine);
        }

        private static void EmitReadByte(NsfCodeBuilder code)
        {
            code.Label("ReadByte");
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, CursorHigh);
            code.Emit(NsfOpcode.CompareAccumulatorImmediate, NsfDataFormat.DataEndAddress >> NsfDataFormat.ByteShift);
            code.Branch(NsfOpcode.BranchNotEqual, "ReadAvailableByte");
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, DataBank);
            code.Emit(NsfOpcode.CompareAccumulatorImmediate, NsfDataFormat.LastDataBank);
            code.Branch(NsfOpcode.BranchEqual, "ReadExhausted");
            code.Emit(NsfOpcode.IncrementZeroPage, DataBank);
            code.Emit(NsfOpcode.LoadAccumulatorZeroPage, DataBank);
            code.Emit(NsfOpcode.StoreAccumulatorAbsolute, NsfDataFormat.BankRegister);
            code.Emit(NsfOpcode.LoadAccumulatorImmediate, NsfDataFormat.DataAddress >> NsfDataFormat.ByteShift);
            code.Emit(NsfOpcode.StoreAccumulatorZeroPage, CursorHigh);
            code.Label("ReadAvailableByte");
            code.Emit(NsfOpcode.LoadYImmediate, 0);
            code.Emit(NsfOpcode.LoadAccumulatorIndirectY, CursorLow);
            // A と X を保持するため、読み出し後のカーソル更新は INC と分岐だけで行う。
            code.Emit(NsfOpcode.IncrementZeroPage, CursorLow);
            code.Branch(NsfOpcode.BranchNotEqual, "ReadReturn");
            code.Emit(NsfOpcode.IncrementZeroPage, CursorHigh);
            code.Label("ReadReturn");
            code.Emit(NsfOpcode.ClearCarry);
            code.Emit(NsfOpcode.ReturnSubroutine);
            code.Label("ReadExhausted");
            code.Emit(NsfOpcode.SetCarry);
            code.Emit(NsfOpcode.ReturnSubroutine);
        }

        private static void ReadOrStop(NsfCodeBuilder code)
        {
            code.Emit(NsfOpcode.JumpSubroutineAbsolute, targetLabel: "ReadByte");
            code.LongBranch(NsfOpcode.BranchCarrySet, "Fail");
        }
    }
}
