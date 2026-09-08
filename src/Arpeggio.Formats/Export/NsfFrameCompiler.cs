using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Export
{
    /// <summary>制御列を PLAY へ量子化し、衝突検証と制御値の集約後に NES レジスタ化する。</summary>
    public sealed class NsfFrameCompiler
    {
        private readonly ConversionReport _report;
        private readonly List<ControlEvent> _events = new List<ControlEvent>();
        private readonly ControlEvent?[] _stops;
        private readonly ControlEvent?[] _values;
        private readonly long[] _lastOnFrames;
        private long _frame = -1;

        private NsfFrameCompiler(int trackCount, ConversionReport report)
        {
            _report = report;
            _stops = new ControlEvent?[trackCount];
            _values = new ControlEvent?[trackCount];
            _lastOnFrames = new long[trackCount];
            Array.Fill(_lastOnFrames, -1);
        }

        /// <summary>既存レポートへ診断を追記する。エラーまたは strict 警告時は部分列を返さない。</summary>
        public static NsfFrameTimeline? Compile(ControlTimeline timeline, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentNullException.ThrowIfNull(report);
            if (timeline.Chip != report.Chip || report.Format != ConversionFormat.Nsf)
            {
                throw new ArgumentException("NSF の同じチップのレポートを指定してください。", nameof(report));
            }
            if (timeline.Chip != ChipKind.Nes)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "NSF は NES の制御列だけを受け付けます。"));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            return new NsfFrameCompiler(timeline.Tracks.Count, report).CompileTimeline(timeline);
        }

        private NsfFrameTimeline? CompileTimeline(ControlTimeline timeline)
        {
            foreach (ControlEvent control in timeline.Events)
            {
                long frame = NsfTiming.Quantize(control.PositionSamples);
                ReportTiming(control, frame);
                if (frame != _frame)
                {
                    FlushFrame();
                    _frame = frame;
                }
                Accumulate(control);
            }
            FlushFrame();
            if (_report.ErrorCount != 0)
            {
                return null;
            }
            long endFrame = NsfTiming.Quantize(timeline.EndSamples);
            var quantized = new ControlTimeline(timeline, NsfTiming.RepresentativeSamples(endFrame), _events);
            RegisterTimeline? registers = NesRegisterCompiler.Compile(quantized, _report);
            if (registers is null)
            {
                return null;
            }
            var writes = new List<NsfRegisterWrite>(registers.Writes.Count);
            foreach (RegisterWrite write in registers.Writes)
            {
                writes.Add(new NsfRegisterWrite(NsfTiming.Quantize(write.PositionSamples), write.Address, write.Value));
            }
            _report.SetStatistic("playFrames", endFrame);
            return new NsfFrameTimeline(endFrame, writes);
        }

        private void Accumulate(ControlEvent control)
        {
            int trackIndex = control.TrackIndex;
            if (control.Kind == ControlEventKind.NoteOn)
            {
                if (_lastOnFrames[trackIndex] == _frame)
                {
                    ReportCollision(control);
                }
                _lastOnFrames[trackIndex] = _frame;
                _values[trackIndex] = control;
            }
            else if (control.Kind == ControlEventKind.NoteOff)
            {
                if (control.Note is not null && _lastOnFrames[trackIndex] == _frame)
                {
                    ReportCollision(control);
                }
                DiscardPendingUpdate(trackIndex);
                _stops[trackIndex] = control;
                _values[trackIndex] = null;
            }
            else if (control.Kind == ControlEventKind.Update)
            {
                MergeUpdate(control);
            }
        }

        private void MergeUpdate(ControlEvent control)
        {
            int trackIndex = control.TrackIndex;
            ControlEventKind kind = control.Kind;
            if (_values[trackIndex] is ControlEvent previous)
            {
                ReportCoalesced(previous);
                kind = previous.Kind;
            }
            // On の trigger は残し、その PLAY で最終的に確定する値だけをレジスタ化する。
            _values[trackIndex] = Copy(control, control.PositionSamples, kind);
        }

        private void DiscardPendingUpdate(int trackIndex)
        {
            if (_values[trackIndex] is ControlEvent previous && previous.Kind == ControlEventKind.Update)
            {
                ReportCoalesced(previous);
            }
        }

        private void FlushFrame()
        {
            if (_frame < 0)
            {
                return;
            }
            long positionSamples = NsfTiming.RepresentativeSamples(_frame);
            AppendEvents(_stops, positionSamples);
            AppendEvents(_values, positionSamples);
        }

        private void AppendEvents(ControlEvent?[] controls, long positionSamples)
        {
            for (int trackIndex = 0; trackIndex < controls.Length; trackIndex++)
            {
                if (controls[trackIndex] is ControlEvent control)
                {
                    _events.Add(Copy(control, positionSamples, control.Kind));
                    controls[trackIndex] = null;
                }
            }
        }

        private void ReportTiming(ControlEvent control, long frame)
        {
            double error = NsfTiming.ErrorMicroseconds(control.PositionSamples, frame);
            if (error == 0)
            {
                return;
            }
            _report.AddWarning(ForControl("NsfTimingQuantized", "制御時刻を NTSC PLAY へ量子化しました。最大誤差の単位は µs です。", control) with
            {
                Original = control.PositionSamples.ToString(CultureInfo.InvariantCulture),
                Converted = frame.ToString(CultureInfo.InvariantCulture), MaximumError = error
            });
        }

        private void ReportCollision(ControlEvent control)
            => _report.AddError(ForControl("NsfEventCollision", "同一トラックの発音境界が同じ PLAY フレームへ潰れます。", control));

        private void ReportCoalesced(ControlEvent control)
            => _report.AddWarning(ForControl("ControlUpdateCoalesced", "同じ PLAY 内の制御値を最終状態へ集約しました。", control));

        private static ConversionDiagnostic ForControl(string code, string message, ControlEvent control)
            => new ConversionDiagnostic(code, message)
            {
                SourceTrack = control.TrackIndex, SourceEvent = control.Note?.SourceEvent,
                SourceTick = control.Note?.Tick, OutputTrack = control.TrackIndex
            };

        private static ControlEvent Copy(ControlEvent control, long positionSamples, ControlEventKind kind)
            => new ControlEvent(positionSamples, control.TrackIndex, kind, control.Note,
                control.Frame, control.MidiNote, control.Volume, control.Duty);
    }
}
