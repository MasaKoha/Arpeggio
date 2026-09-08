using System;
using System.Collections.Generic;
using System.Globalization;

namespace Arpeggio.Formats.Midi
{
    /// <summary>全トラックのテンポを統合し、PPQN 共通分母の整数分子で絶対実時間を照会する。</summary>
    public sealed class MidiTempoMap
    {
        internal const long MicrosecondsPerSecond = 1000000;
        internal const long MicrosecondsPerMinute = 60 * MicrosecondsPerSecond;
        private const int DefaultMicrosecondsPerQuarter = 500000;
        private const int MinimumTempo = 1;
        private const int MaximumTempo = 1000;
        private readonly MidiTempoSegment[] _segments;

        private MidiTempoMap(MidiFile file, List<MidiTempoSegment> segments, int outputTempo)
        {
            _segments = segments.ToArray();
            TicksPerQuarterNote = file.TicksPerQuarterNote;
            EndTick = file.EndTick;
            OutputTempoBpm = outputTempo;
            foreach (MidiEvent current in file.Events)
            {
                if (current.Kind == MidiMessageKind.EndOfTrack && current.Tick == EndTick)
                {
                    EndEvent = current;
                }
            }
        }

        /// <summary>実時間分子をマイクロ秒へ戻す共通分母。</summary>
        public int TicksPerQuarterNote { get; }
        /// <summary>入力全体の最遅 EOT tick。</summary>
        public long EndTick { get; }
        /// <summary>実時間を焼き込む固定の整数 BPM。</summary>
        public int OutputTempoBpm { get; }
        internal MidiEvent EndEvent { get; }
        internal long MaximumTimeNumerator => ConversionLimits.MaximumDurationSeconds * MicrosecondsPerSecond * TicksPerQuarterNote;

        /// <summary>基準 BPM とテンポ診断を確定する。入力・設定エラー時は null、strict 警告時も変換を続行できる。</summary>
        public static MidiTempoMap? Create(MidiFile file, MidiImportOptions options, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Midi)
            {
                throw new ArgumentException("MIDI のレポートが必要です。", nameof(report));
            }
            if (options.Tempo is int tempo && (tempo < MinimumTempo || tempo > MaximumTempo))
            {
                report.AddError(new ConversionDiagnostic("InvalidOptions", "tempo は 1〜1000 を指定してください。"));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            try
            {
                List<MidiTempoSegment> segments = BuildSegments(file, report);
                int outputTempo = ChooseOutputTempo(segments[0].MicrosecondsPerQuarter, options.Tempo, report);
                var map = new MidiTempoMap(file, segments, outputTempo);
                if (map.GetTimeNumerator(file.EndTick) > map.MaximumTimeNumerator)
                {
                    report.AddError(new ConversionDiagnostic("DurationLimitExceeded", "MIDI の演奏時間が 1800 秒を超えています。"));
                    return null;
                }
                return map;
            }
            catch (OverflowException)
            {
                report.AddError(new ConversionDiagnostic("MidiTimeOverflow", "MIDI の実時間積算が整数範囲を超えています。"));
                return null;
            }
        }

        /// <summary>非負の絶対 MIDI tick を実時間の整数分子へ変換する。EOT 後は最後のテンポを延長し、整数範囲超過は例外とする。</summary>
        public long GetTimeNumerator(long midiTick)
        {
            if (midiTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(midiTick));
            }
            int lower = 0;
            int upper = _segments.Length;
            while (lower + 1 < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (_segments[middle].Tick <= midiTick)
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }
            MidiTempoSegment segment = _segments[lower];
            return checked(segment.TimeNumerator + checked((midiTick - segment.Tick) * segment.MicrosecondsPerQuarter));
        }

        private static List<MidiTempoSegment> BuildSegments(MidiFile file, ConversionReport report)
        {
            var segments = new List<MidiTempoSegment> { new MidiTempoSegment(0, 0, DefaultMicrosecondsPerQuarter) };
            MidiEvent? pending = null;
            bool conflicting = false;
            foreach (MidiEvent current in file.Events)
            {
                if (current.Kind != MidiMessageKind.Tempo)
                {
                    continue;
                }
                if (file.Format == 1 && current.SourceTrack != 0)
                {
                    report.AddWarning(current.Diagnose("NonConductorTempo", "track 0 以外の Tempo も採用しました。"));
                }
                if (pending is MidiEvent previous)
                {
                    if (previous.Tick == current.Tick)
                    {
                        conflicting |= previous.DataOne != current.DataOne;
                    }
                    else
                    {
                        AppendTempo(segments, previous, conflicting, report);
                        conflicting = false;
                    }
                }
                pending = current;
            }
            if (pending is MidiEvent last)
            {
                AppendTempo(segments, last, conflicting, report);
            }
            return segments;
        }

        private static void AppendTempo(List<MidiTempoSegment> segments, MidiEvent source, bool conflicting, ConversionReport report)
        {
            MidiTempoSegment previous = segments[segments.Count - 1];
            if (conflicting)
            {
                report.AddWarning(source.Diagnose("ConflictingTempo", "同 tick の競合テンポは元イベント順で最後の値を採用しました。"));
            }
            if (source.Tick == 0)
            {
                segments[0] = new MidiTempoSegment(0, 0, source.DataOne);
                return;
            }
            if (source.DataOne == previous.MicrosecondsPerQuarter)
            {
                return;
            }
            long numerator = checked(previous.TimeNumerator + checked((source.Tick - previous.Tick) * previous.MicrosecondsPerQuarter));
            segments.Add(new MidiTempoSegment(source.Tick, numerator, source.DataOne));
            report.AddWarning(source.Diagnose("TempoMapFlattened", "実効テンポ変化を固定 BPM の絶対ノート位置へ焼き込みます。") with
            {
                Original = previous.MicrosecondsPerQuarter.ToString(CultureInfo.InvariantCulture),
                Converted = source.DataOne.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static int ChooseOutputTempo(int microsecondsPerQuarter, int? explicitTempo, ConversionReport report)
        {
            if (explicitTempo is int tempo)
            {
                return tempo;
            }
            decimal original = (decimal)MicrosecondsPerMinute / microsecondsPerQuarter;
            int converted = (int)Math.Clamp(Math.Round(original, MidpointRounding.AwayFromZero), MinimumTempo, MaximumTempo);
            if ((long)converted * microsecondsPerQuarter != MicrosecondsPerMinute)
            {
                report.AddWarning(new ConversionDiagnostic("TempoRounded", "先頭テンポを 1〜1000 の整数 BPM に丸めました。")
                {
                    SourceTick = 0, Original = original.ToString(CultureInfo.InvariantCulture),
                    Converted = converted.ToString(CultureInfo.InvariantCulture)
                });
            }
            return converted;
        }
    }
}
