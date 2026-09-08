using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Formats.Export
{
    /// <summary>GB の Pulse 二声と Wave を、副作用を保持したレジスタ列へ変換する。</summary>
    public sealed class GameBoyRegisterCompiler
    {
        private readonly ConversionReport _report;
        private readonly GameBoyRegisterValues _values;
        private readonly List<RegisterWrite> _writes = new List<RegisterWrite>();
        private readonly GameBoyRegisterChannel _pulseOne = new GameBoyRegisterChannel(GameBoyRegisters.PulseOneDuty,
            GameBoyRegisters.PulseOneEnvelope, GameBoyRegisters.PulseOneFrequencyLow,
            GameBoyRegisters.PulseOneFrequencyHigh, GameBoyRegisters.PulseOneRouting);
        private readonly GameBoyRegisterChannel _pulseTwo = new GameBoyRegisterChannel(GameBoyRegisters.PulseTwoDuty,
            GameBoyRegisters.PulseTwoEnvelope, GameBoyRegisters.PulseTwoFrequencyLow,
            GameBoyRegisters.PulseTwoFrequencyHigh, GameBoyRegisters.PulseTwoRouting);
        private readonly GameBoyRegisterChannel _wave = new GameBoyRegisterChannel(GameBoyRegisters.WaveLength,
            GameBoyRegisters.WaveVolume, GameBoyRegisters.WaveFrequencyLow,
            GameBoyRegisters.WaveFrequencyHigh, GameBoyRegisters.WaveRouting);
        private byte _routing;
        private bool _writeLimitExceeded;

        private GameBoyRegisterCompiler(ConversionReport report)
        {
            _report = report;
            _values = new GameBoyRegisterValues(report);
        }

        /// <summary>制御列を変換し同じチップのレポートへ診断を追記する。エラーまたは strict 警告時は null。現段階では Noise を無視し、Pulse envelope は初期値を保持する。</summary>
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
            if (timeline.Chip != ChipKind.GameBoy)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "GB レジスタ変換は Game Boy の制御列だけを受け付けます。"));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var compiler = new GameBoyRegisterCompiler(report);
            return compiler.CompileTimeline(timeline);
        }

        private RegisterTimeline? CompileTimeline(ControlTimeline timeline)
        {
            _report.AddLimitation("実機周期・位相・DAC・ミキサーの差により既存 PCM とは一致しません。Pulse の再トリガーだけで duty 位相を任意値へ戻せません。");
            _report.AddLimitation("Wave の右シフト音量と DAC は、既存の符号付き浮動小数乗算とは異なります。");
            _report.AddLimitation("現段階では GB Noise と時間経過による Pulse envelope の増減は未対応で、Pulse の初期 envelope 音量を保持します。");
            Initialize();
            foreach (ControlEvent control in timeline.Events)
            {
                CompileEvent(control, timeline);
                if (_writeLimitExceeded)
                {
                    break;
                }
            }
            StopAll(timeline.EndSamples);
            _report.SetStatistic("registerWrites", _writes.Count);
            return _report.CanWrite ? new RegisterTimeline(timeline.Chip, timeline.EndSamples, _writes) : null;
        }

        private void Initialize()
        {
            Write(0, GameBoyRegisters.Power, 0);
            Write(0, GameBoyRegisters.Power, GameBoyRegisters.PowerEnabled);
            Write(0, GameBoyRegisters.MasterVolume, GameBoyRegisters.MasterVolumeMaximum);
            Write(0, GameBoyRegisters.Routing, 0);
            Write(0, GameBoyRegisters.PulseOneSweep, 0);
        }

        private void CompileEvent(ControlEvent control, ControlTimeline timeline)
        {
            ControlTrack track = timeline.Tracks[control.TrackIndex];
            if (track.Channel != ChannelKind.Pulse && track.Channel != ChannelKind.Wave)
            {
                return;
            }
            GameBoyRegisterChannel channel = SelectChannel(track);
            if (control.Kind == ControlEventKind.NoteOff)
            {
                StopChannel(control.PositionSamples, channel, track.Channel);
                return;
            }
            if (track.Muted || control.Kind == ControlEventKind.None)
            {
                return;
            }
            if (control.Kind == ControlEventKind.NoteOn)
            {
                _values.ReportPan(control, track.Pan);
            }
            ControlInstrument instrument = timeline.Instruments[control.Note!.InstrumentId];
            int frequency = _values.CalculateFrequencyRegister(control, track.Channel);
            byte routing = GameBoyRegisterValues.CalculateRouting(track.Pan, channel.RoutingMask);
            if (track.Channel == ChannelKind.Wave)
            {
                CompileWave(control, instrument, frequency, routing);
                return;
            }
            CompilePulse(control, instrument, channel, frequency, routing);
        }

        private GameBoyRegisterChannel SelectChannel(ControlTrack track)
        {
            if (track.Channel == ChannelKind.Wave)
            {
                return _wave;
            }
            return track.ChannelIndex == 0 ? _pulseOne : _pulseTwo;
        }

        private void CompilePulse(ControlEvent control, ControlInstrument instrument,
            GameBoyRegisterChannel channel, int frequency, byte routing)
        {
            bool isOnset = control.Kind == ControlEventKind.NoteOn;
            int volume = _values.CalculatePulseVolume(control, instrument);
            bool needsTrigger = volume > 0 && (isOnset || channel.Volume != volume);
            if (needsTrigger)
            {
                SetRouting(control.PositionSamples, channel, 0);
                Write(control.PositionSamples, channel.VolumeAddress, 0);
            }
            else if (volume == 0 && (isOnset || channel.Volume != 0))
            {
                StopChannel(control.PositionSamples, channel, ChannelKind.Pulse);
            }
            WritePulseDuty(control, channel);
            // 同時に音量も変わる場合、trigger より先に新周期の上下を設定する。
            WriteFrequency(control.PositionSamples, channel, frequency, isOnset);
            if (needsTrigger)
            {
                Write(control.PositionSamples, channel.VolumeAddress, (byte)(volume << GameBoyRegisters.EnvelopeVolumeShift));
                WriteTrigger(control.PositionSamples, channel, frequency);
                SetRouting(control.PositionSamples, channel, routing);
                if (!isOnset)
                {
                    _values.ReportRetrigger(control);
                }
            }
            channel.Volume = volume;
        }

        private void CompileWave(ControlEvent control, ControlInstrument instrument, int frequency, byte routing)
        {
            bool isOnset = control.Kind == ControlEventKind.NoteOn;
            if (isOnset)
            {
                SetRouting(control.PositionSamples, _wave, 0);
                Write(control.PositionSamples, GameBoyRegisters.WaveDac, 0);
                WriteWaveRam(control.PositionSamples, instrument);
            }
            int volume = _values.CalculateWaveVolume(control, instrument);
            if (isOnset || _wave.Volume != volume)
            {
                Write(control.PositionSamples, GameBoyRegisters.WaveVolume, (byte)volume);
                _wave.Volume = volume;
            }
            WriteFrequencyLow(control.PositionSamples, _wave, frequency, isOnset);
            if (isOnset)
            {
                Write(control.PositionSamples, GameBoyRegisters.WaveDac, GameBoyRegisters.DacEnabled);
                WriteTrigger(control.PositionSamples, _wave, frequency);
                SetRouting(control.PositionSamples, _wave, routing);
                return;
            }
            WriteFrequencyHigh(control.PositionSamples, _wave, frequency, false);
        }

        private void WritePulseDuty(ControlEvent control, GameBoyRegisterChannel channel)
        {
            int duty = (control.Duty - (int)DutyCycle.Percent12_5) << GameBoyRegisters.DutyShift;
            if (control.Kind == ControlEventKind.NoteOn || channel.Duty != duty)
            {
                Write(control.PositionSamples, channel.LengthDutyAddress, (byte)duty);
                channel.Duty = duty;
            }
        }

        private void WriteWaveRam(long positionSamples, ControlInstrument instrument)
        {
            for (int byteIndex = 0; byteIndex < GameBoyRegisters.WaveRamBytes; byteIndex++)
            {
                int sampleIndex = byteIndex * GameBoyRegisters.SamplesPerWaveByte;
                int value = (instrument.Waveform[sampleIndex] << GameBoyRegisters.WaveSampleShift) | instrument.Waveform[sampleIndex + 1];
                Write(positionSamples, (ushort)(GameBoyRegisters.WaveRamStart + byteIndex), (byte)value);
            }
        }

        private void WriteFrequency(long positionSamples, GameBoyRegisterChannel channel, int frequency, bool force)
        {
            WriteFrequencyLow(positionSamples, channel, frequency, force);
            WriteFrequencyHigh(positionSamples, channel, frequency, force);
        }

        private void WriteFrequencyLow(long positionSamples, GameBoyRegisterChannel channel, int frequency, bool force)
        {
            int low = frequency & GameBoyRegisters.FrequencyLowMask;
            if (force || channel.FrequencyLow != low)
            {
                Write(positionSamples, channel.FrequencyLowAddress, (byte)low);
                channel.FrequencyLow = low;
            }
        }

        private void WriteFrequencyHigh(long positionSamples, GameBoyRegisterChannel channel, int frequency, bool force)
        {
            int high = (frequency >> GameBoyRegisters.FrequencyHighShift) & GameBoyRegisters.FrequencyHighMask;
            if (force || channel.FrequencyHigh != high)
            {
                Write(positionSamples, channel.FrequencyHighAddress, (byte)high);
                channel.FrequencyHigh = high;
            }
        }

        private void WriteTrigger(long positionSamples, GameBoyRegisterChannel channel, int frequency)
        {
            int high = (frequency >> GameBoyRegisters.FrequencyHighShift) & GameBoyRegisters.FrequencyHighMask;
            Write(positionSamples, channel.FrequencyHighAddress, (byte)(high | GameBoyRegisters.Trigger));
            channel.FrequencyHigh = high;
        }

        private void StopChannel(long positionSamples, GameBoyRegisterChannel channel, ChannelKind kind)
        {
            ushort dacAddress = kind == ChannelKind.Wave ? GameBoyRegisters.WaveDac : channel.VolumeAddress;
            Write(positionSamples, dacAddress, 0);
            SetRouting(positionSamples, channel, 0);
            channel.Volume = 0;
        }

        private void SetRouting(long positionSamples, GameBoyRegisterChannel channel, byte routing)
        {
            byte value = (byte)((_routing & ~channel.RoutingMask) | routing);
            if (_routing != value)
            {
                Write(positionSamples, GameBoyRegisters.Routing, value);
                _routing = value;
            }
        }

        private void StopAll(long positionSamples)
        {
            Write(positionSamples, GameBoyRegisters.Routing, 0);
            Write(positionSamples, GameBoyRegisters.PulseOneEnvelope, 0);
            Write(positionSamples, GameBoyRegisters.PulseTwoEnvelope, 0);
            Write(positionSamples, GameBoyRegisters.NoiseEnvelope, 0);
            Write(positionSamples, GameBoyRegisters.WaveDac, 0);
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
