using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Formats.Export
{
    /// <summary>NES の Pulse 二声と Triangle を、副作用を保持したレジスタ列へ変換する。</summary>
    public sealed class NesRegisterCompiler
    {
        private const double ClockRate = 1789773;
        private const int PulseDivider = 16;
        private const int TriangleDivider = 32;
        private const int MinimumTimer = 8;
        private const int MaximumTimer = 2047;
        private const int MaximumVolume = 15;
        private readonly ConversionReport _report;
        private readonly List<RegisterWrite> _writes = new List<RegisterWrite>();
        private readonly NesRegisterChannel _pulseOne = new NesRegisterChannel(NesRegisters.PulseOneControl,
            NesRegisters.PulseOneTimerLow, NesRegisters.PulseOneTimerHigh, NesRegisters.PulseOneEnable);
        private readonly NesRegisterChannel _pulseTwo = new NesRegisterChannel(NesRegisters.PulseTwoControl,
            NesRegisters.PulseTwoTimerLow, NesRegisters.PulseTwoTimerHigh, NesRegisters.PulseTwoEnable);
        private readonly NesRegisterChannel _triangle = new NesRegisterChannel(NesRegisters.TriangleLinear,
            NesRegisters.TriangleTimerLow, NesRegisters.TriangleTimerHigh, NesRegisters.TriangleEnable);
        private byte _enabledChannels;
        private bool _writeLimitExceeded;

        private NesRegisterCompiler(ConversionReport report)
        {
            _report = report;
        }

        /// <summary>制御列を変換して同じチップのレポートへ診断を追記する。エラーまたは strict 警告時は部分列を返さない。Noise / DPCM は現段階では無視する。</summary>
        public static RegisterTimeline? Compile(ControlTimeline timeline, ConversionReport report)
        {
            if (timeline is null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }
            if (timeline.Chip != report.Chip)
            {
                throw new ArgumentException("制御列とレポートのチップが一致していません。", nameof(report));
            }
            if (timeline.Chip != ChipKind.Nes)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "NES レジスタ変換は NES の制御列だけを受け付けます。"));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var compiler = new NesRegisterCompiler(report);
            return compiler.CompileTimeline(timeline);
        }

        private RegisterTimeline? CompileTimeline(ControlTimeline timeline)
        {
            _report.AddLimitation("実機周期への量子化・位相・DAC・ミキサーの差により、既存の PCM 出力とは一致しません。");
            _report.AddLimitation("Triangle の位相を NoteOn で任意値へ戻せません。停止後は DAC 値を保持し、フレームカウンターの即時クロックにも物理的遅延があります。");
            Initialize();
            foreach (ControlEvent control in timeline.Events)
            {
                CompileEvent(control, timeline.Tracks[control.TrackIndex]);
                if (_writeLimitExceeded)
                {
                    break;
                }
            }
            _report.SetStatistic("registerWrites", _writes.Count);
            return _report.CanWrite ? new RegisterTimeline(timeline.Chip, timeline.EndSamples, _writes) : null;
        }

        private void Initialize()
        {
            Write(0, NesRegisters.Status, 0);
            Write(0, NesRegisters.DmcControl, 0);
            Write(0, NesRegisters.DmcOutput, 0);
            Write(0, NesRegisters.PulseOneSweep, NesRegisters.SweepDisabled);
            Write(0, NesRegisters.PulseTwoSweep, NesRegisters.SweepDisabled);
            Write(0, NesRegisters.FrameCounter, NesRegisters.FiveStepInterruptDisabled);
        }

        private void CompileEvent(ControlEvent control, ControlTrack track)
        {
            if (track.Channel != ChannelKind.Pulse && track.Channel != ChannelKind.Triangle)
            {
                return;
            }
            NesRegisterChannel channel = SelectChannel(track);
            if (control.Kind == ControlEventKind.NoteOff)
            {
                _enabledChannels = (byte)(_enabledChannels & ~channel.EnableMask);
                Write(control.PositionSamples, NesRegisters.Status, _enabledChannels);
                return;
            }
            if (track.Muted || control.Kind == ControlEventKind.None)
            {
                return;
            }
            bool isOnset = control.Kind == ControlEventKind.NoteOn;
            int timer = CalculateTimer(control, track.Channel);
            if (isOnset)
            {
                // length load より先に有効化しないと、停止中のカウンターを再ロードできない。
                _enabledChannels |= channel.EnableMask;
                Write(control.PositionSamples, NesRegisters.Status, _enabledChannels);
            }
            if (track.Channel == ChannelKind.Pulse)
            {
                WritePulseControl(control, channel, isOnset);
            }
            else if (isOnset)
            {
                Write(control.PositionSamples, NesRegisters.TriangleLinear, NesRegisters.TriangleLinearHeld);
            }
            WriteTimer(control.PositionSamples, channel, timer, isOnset);
            if (track.Channel == ChannelKind.Triangle && isOnset)
            {
                // high の reload flag を即時クロックへ渡し、最初の linear counter をロードする。
                Write(control.PositionSamples, NesRegisters.FrameCounter, NesRegisters.FiveStepInterruptDisabled);
            }
        }

        private NesRegisterChannel SelectChannel(ControlTrack track)
        {
            if (track.Channel == ChannelKind.Triangle)
            {
                return _triangle;
            }
            return track.ChannelIndex == 0 ? _pulseOne : _pulseTwo;
        }

        private void WritePulseControl(ControlEvent control, NesRegisterChannel channel, bool isOnset)
        {
            int volume = (int)Math.Round(MaximumVolume * control.Volume, MidpointRounding.AwayFromZero);
            int dutyBits = control.Duty - (int)DutyCycle.Percent12_5;
            int value = (dutyBits << NesRegisters.DutyShift) | NesRegisters.LengthHaltConstantVolume | volume;
            if (isOnset || channel.Control != value)
            {
                Write(control.PositionSamples, channel.ControlAddress, (byte)value);
                channel.Control = value;
            }
        }

        private void WriteTimer(long positionSamples, NesRegisterChannel channel, int timer, bool isOnset)
        {
            int low = timer & NesRegisters.TimerLowMask;
            int high = (timer >> NesRegisters.TimerHighShift) & NesRegisters.TimerHighMask;
            if (isOnset || channel.TimerLow != low)
            {
                Write(positionSamples, channel.TimerLowAddress, (byte)low);
                channel.TimerLow = low;
            }
            if (isOnset || channel.TimerHigh != high)
            {
                Write(positionSamples, channel.TimerHighAddress, (byte)(high | NesRegisters.LengthIndexZero));
                channel.TimerHigh = high;
            }
        }

        private int CalculateTimer(ControlEvent control, ChannelKind channel)
        {
            int divider = channel == ChannelKind.Triangle ? TriangleDivider : PulseDivider;
            double clampedMidiNote = PitchTable.ClampMidiNote(ChipKind.Nes, channel, control.MidiNote);
            double originalFrequency = PitchTable.GetFrequency(control.MidiNote);
            double minimumFrequency = ClockRate / (divider * (MaximumTimer + 1.0));
            double maximumFrequency = ClockRate / (divider * (MinimumTimer + 1.0));
            if (originalFrequency < minimumFrequency || originalFrequency > maximumFrequency)
            {
                AddPitchWarning(control, clampedMidiNote);
            }
            double frequency = PitchTable.GetFrequency(clampedMidiNote);
            double timer = Math.Round(ClockRate / (divider * frequency) - 1, MidpointRounding.ToEven);
            return (int)Math.Clamp(timer, MinimumTimer, MaximumTimer);
        }

        private void AddPitchWarning(ControlEvent control, double clampedMidiNote)
        {
            ControlNote note = control.Note!;
            _report.AddWarning(new ConversionDiagnostic("PitchClamped", "変調後の音程を NES チャンネルの連続音域へ制限しました。")
            {
                SourceTrack = control.TrackIndex,
                SourceEvent = note.SourceEvent,
                SourceTick = note.Tick,
                OutputTrack = control.TrackIndex,
                Original = control.MidiNote.ToString("R", CultureInfo.InvariantCulture),
                Converted = clampedMidiNote.ToString("R", CultureInfo.InvariantCulture),
                MaximumError = Math.Abs(control.MidiNote - clampedMidiNote)
            });
        }

        private void Write(long positionSamples, ushort address, byte value)
        {
            if (_writeLimitExceeded)
            {
                return;
            }
            if (_writes.Count == ConversionLimits.MaximumRegisterWrites)
            {
                _writeLimitExceeded = true;
                _report.AddError(new ConversionDiagnostic("RegisterWriteLimitExceeded", "レジスタ書き込み数の上限を超えています。"));
                return;
            }
            _writes.Add(new RegisterWrite(positionSamples, _writes.Count, address, value));
        }
    }
}
