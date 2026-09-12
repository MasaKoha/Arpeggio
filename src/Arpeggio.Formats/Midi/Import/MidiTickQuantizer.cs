using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Midi.Import.Tempo;

namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>絶対実時間を固定 BPM のグリッドへ量子化し、短音の延長と最大誤差を診断する。</summary>
    public sealed class MidiTickQuantizer
    {
        private readonly MidiTempoMap _tempoMap;
        private readonly ConversionReport _report;

        private MidiTickQuantizer(MidiTempoMap tempoMap, int grid, ConversionReport report)
        {
            _tempoMap = tempoMap;
            _report = report;
            Grid = grid;
            InputEndTick = QuantizeTime(tempoMap.GetTimeNumerator(tempoMap.EndTick), tempoMap.EndEvent);
        }

        /// <summary>48 の正の約数である量子化単位。</summary>
        public int Grid { get; }
        /// <summary>入力 EOT の絶対実時間を量子化した終端。</summary>
        public int InputEndTick { get; }

        /// <summary>量子化設定を検証する。設定・先行エラー時は null、strict 警告では継続する。</summary>
        public static MidiTickQuantizer? Create(MidiTempoMap tempoMap, MidiImportOptions options, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(tempoMap);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Midi)
            {
                throw new ArgumentException("MIDI のレポートが必要です。", nameof(report));
            }
            if (options.QuantizeTicks <= 0 || Song.FixedTicksPerBeat % options.QuantizeTicks != 0)
            {
                report.AddError(new ConversionDiagnostic("InvalidOptions", "quantizeTicks は 48 の正の約数を指定してください。"));
            }
            return report.ErrorCount == 0 ? new MidiTickQuantizer(tempoMap, options.QuantizeTicks, report) : null;
        }

        /// <summary>収集済み旋律の元 gate を量子化する。打楽器は固定実時間終端を渡すオーバーロードを使う。</summary>
        public MidiQuantizedNote? QuantizeNote(MidiNote note)
        {
            ArgumentNullException.ThrowIfNull(note);
            if (note.IsDrum)
            {
                throw new ArgumentException("打楽器は固定実時間 gate を指定してください。", nameof(note));
            }
            return QuantizeNote(note, _tempoMap.GetTimeNumerator(note.EndTick));
        }

        /// <summary>音色変換で確定した実時間終端を量子化する。終端の単位は MidiTempoMap と同じ整数分子。</summary>
        public MidiQuantizedNote? QuantizeNote(MidiNote note, long endTimeNumerator)
        {
            ArgumentNullException.ThrowIfNull(note);
            if (_report.ErrorCount != 0)
            {
                return null;
            }
            long startTimeNumerator = _tempoMap.GetTimeNumerator(note.StartTick);
            if (endTimeNumerator < startTimeNumerator || endTimeNumerator > _tempoMap.MaximumTimeNumerator)
            {
                string code = endTimeNumerator < startTimeNumerator ? "InvalidMidiGate" : "DurationLimitExceeded";
                _report.AddError(note.Source.Diagnose(code, "発音終端が開始より前、または実時間上限を超えています。"));
                return null;
            }
            if (endTimeNumerator == startTimeNumerator)
            {
                _report.AddWarning(note.Source.Diagnose("ZeroLengthNoteDropped", "量子化前に長さ 0 の発音を破棄しました。"));
                return null;
            }
            int startTick = QuantizeTime(startTimeNumerator, note.Source);
            int endTick = QuantizeTime(endTimeNumerator, note.Source);
            if (endTick == startTick)
            {
                endTick = checked(startTick + Grid);
                _report.AddWarning(note.Source.Diagnose("ShortNoteExtended", "正の短音が潰れたため終端を 1 グリッド延長しました。") with
                {
                    OutputTick = startTick, Original = "0", Converted = Grid.ToString(CultureInfo.InvariantCulture)
                });
            }
            return new MidiQuantizedNote(note, startTick, endTick);
        }

        /// <summary>声割り当て後の全終端と入力 EOT から曲長を求める。空音判定は割り当て段階が担当する。</summary>
        public int GetLengthTicks(IEnumerable<int> outputEndTicks)
        {
            ArgumentNullException.ThrowIfNull(outputEndTicks);
            int length = Math.Max(Song.FixedTicksPerBeat, InputEndTick);
            foreach (int endTick in outputEndTicks)
            {
                if (endTick < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(outputEndTicks));
                }
                length = Math.Max(length, endTick);
            }
            return length;
        }

        private int QuantizeTime(long timeNumerator, MidiEvent source)
        {
            // 乗算は正確に行い、グリッドへ移す最後の除算だけで丸める。
            decimal scaled = (decimal)timeNumerator * _tempoMap.OutputTempoBpm * Song.FixedTicksPerBeat;
            decimal denominator = MidiTempoMap.MicrosecondsPerMinute * _tempoMap.TicksPerQuarterNote;
            int converted = checked((int)(Math.Round(scaled / (denominator * Grid), MidpointRounding.AwayFromZero) * Grid));
            decimal errorNumerator = Math.Abs(scaled - converted * denominator);
            if (errorNumerator != 0)
            {
                _report.AddWarning(source.Diagnose("MidiTimingQuantized", "絶対実時間をグリッドへ丸めました。最大誤差の単位は出力 tick です。") with
                {
                    OutputTick = converted,
                    Original = (scaled / denominator).ToString(CultureInfo.InvariantCulture),
                    Converted = converted.ToString(CultureInfo.InvariantCulture),
                    MaximumError = (double)(errorNumerator / denominator)
                });
            }
            return converted;
        }
    }
}
