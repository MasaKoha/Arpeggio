using System;

namespace Arpeggio.Formats.Export
{
    /// <summary>PLAY 列を容量検証後に WRITE・WAIT・END へ符号化する。</summary>
    public static class NsfDataEncoder
    {
        /// <summary>順序と許可アドレスを検証し、エラーまたは strict 警告時は部分データを返さない。</summary>
        public static NsfEncodedData? Encode(NsfFrameTimeline timeline, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Nsf || report.Chip != Arpeggio.Core.Document.ChipKind.Nes)
            {
                throw new ArgumentException("NES NSF のレポートを指定してください。", nameof(report));
            }
            if (report.ErrorCount != 0 || !ValidateTimeline(timeline, report))
            {
                return null;
            }
            long dataBytes = EstimateDataBytes(timeline);
            long outputBytes = EstimateOutputBytes(dataBytes);
            ConversionLimits.ValidateExportSize(report, timeline.Writes.Count, outputBytes, dataBytes);
            report.SetOutputMetrics(timeline.EndFrame * NsfTiming.PlayMicroseconds /
                (double)NsfTiming.MicrosecondsPerSecond, outputBytes);
            report.SetStatistic("nsfDataBytes", dataBytes);
            if (!report.CanWrite)
            {
                return null;
            }
            var bytes = new byte[(int)dataBytes];
            int cursor = 0;
            long previousFrame = 0;
            int writesInFrame = 0;
            int maximumWrites = 0;
            foreach (NsfRegisterWrite write in timeline.Writes)
            {
                if (write.Frame != previousFrame)
                {
                    writesInFrame = 0;
                }
                WriteWait(bytes, ref cursor, write.Frame - previousFrame);
                bytes[cursor++] = NsfDataFormat.Write;
                bytes[cursor++] = (byte)(write.Address - NsfDataFormat.RegisterBase);
                bytes[cursor++] = write.Value;
                previousFrame = write.Frame;
                maximumWrites = Math.Max(maximumWrites, ++writesInFrame);
            }
            WriteWait(bytes, ref cursor, timeline.EndFrame - previousFrame);
            bytes[cursor] = NsfDataFormat.End;
            return new NsfEncodedData(bytes, timeline.EndFrame, outputBytes, maximumWrites);
        }

        /// <summary>正しい時刻順の列について、確保せずに END を含むデータ長を算定する。</summary>
        public static long EstimateDataBytes(NsfFrameTimeline timeline)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            long bytes = NsfDataFormat.EndBytes;
            long previousFrame = 0;
            foreach (NsfRegisterWrite write in timeline.Writes)
            {
                bytes = checked(bytes + WaitBytes(write.Frame - previousFrame) + NsfDataFormat.OperandCommandBytes);
                previousFrame = write.Frame;
            }
            return checked(bytes + WaitBytes(timeline.EndFrame - previousFrame));
        }

        /// <summary>END を含む正のデータ長から、ヘッダーと 4 KiB 整列 ROM の長さを算定する。</summary>
        public static long EstimateOutputBytes(long dataBytes)
        {
            if (dataBytes < NsfDataFormat.EndBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(dataBytes));
            }
            long dataBanks = (dataBytes - 1) / ConversionLimits.NsfPlayerBytes + 1;
            return checked(ConversionLimits.NsfHeaderBytes + (dataBanks + 1) * ConversionLimits.NsfPlayerBytes);
        }

        private static bool ValidateTimeline(NsfFrameTimeline timeline, ConversionReport report)
        {
            long maximumFrame = NsfTiming.Quantize((long)ConversionLimits.MaximumDurationSeconds * ConversionLimits.ControlSampleRate);
            if (timeline.EndFrame < 0 || timeline.EndFrame > maximumFrame)
            {
                report.AddError(new ConversionDiagnostic("DurationLimitExceeded", "NSF の終端 PLAY 番号が許容範囲外です。"));
                return false;
            }
            long previousFrame = 0;
            foreach (NsfRegisterWrite write in timeline.Writes)
            {
                if (write.Frame < previousFrame || write.Frame > timeline.EndFrame || !NsfDataFormat.IsAllowedAddress(write.Address))
                {
                    report.AddError(new ConversionDiagnostic("InvalidNsfData", "NSF 書き込みの時刻順・終端・レジスタアドレスが不正です。"));
                    return false;
                }
                previousFrame = write.Frame;
            }
            return true;
        }

        private static long WaitBytes(long frames)
        {
            if (frames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frames));
            }
            long commands = frames == 0 ? 0 : (frames - 1) / NsfDataFormat.MaximumWait + 1;
            return checked(commands * NsfDataFormat.OperandCommandBytes);
        }

        private static void WriteWait(byte[] bytes, ref int cursor, long frames)
        {
            while (frames > 0)
            {
                int wait = (int)Math.Min(frames, NsfDataFormat.MaximumWait);
                bytes[cursor++] = NsfDataFormat.Wait;
                bytes[cursor++] = (byte)(wait & NsfDataFormat.ByteMask);
                bytes[cursor++] = (byte)(wait >> NsfDataFormat.ByteShift);
                frames -= wait;
            }
        }
    }
}
