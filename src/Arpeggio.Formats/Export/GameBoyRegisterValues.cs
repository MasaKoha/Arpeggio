using System;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Formats.Export
{
    /// <summary>GB の連続制御値をレジスタ値へ量子化し、変換損失を元ノートへ記録する。</summary>
    internal sealed class GameBoyRegisterValues
    {
        private const double PulseClock = 131072;
        private const double WaveClock = 65536;
        private const int MinimumPeriod = 1;
        private const int MaximumPeriod = 2048;
        private const int MaximumVolume = 15;
        private const double PercentScale = 100;
        private const double VolumeTolerance = 1e-9;
        private const double PanThreshold = 0.5;
        private const double LeftPan = -1;
        private const double CenterPan = 0;
        private const double RightPan = 1;
        private const int MidpointDivisor = 2;
        private const double QuarterVolume = 0.25;
        private const double HalfVolume = 0.5;
        private const double FullVolume = 1;
        private const int QuarterVolumeRegister = 0x60;
        private const int HalfVolumeRegister = 0x40;
        private const int FullVolumeRegister = 0x20;
        private readonly ConversionReport _report;

        internal GameBoyRegisterValues(ConversionReport report)
        {
            _report = report;
        }

        internal int CalculateFrequencyRegister(ControlEvent control, ChannelKind channel)
        {
            double clock = channel == ChannelKind.Wave ? WaveClock : PulseClock;
            double clampedMidiNote = PitchTable.ClampMidiNote(ChipKind.GameBoy, channel, control.MidiNote);
            double originalFrequency = PitchTable.GetFrequency(control.MidiNote);
            if (originalFrequency < clock / MaximumPeriod || originalFrequency > clock / MinimumPeriod)
            {
                AddWarning(control, "PitchClamped", "変調後の音程を GB チャンネルの連続音域へ制限しました。",
                    control.MidiNote, clampedMidiNote);
            }
            double frequency = PitchTable.GetFrequency(clampedMidiNote);
            int period = (int)Math.Clamp(Math.Round(clock / frequency, MidpointRounding.ToEven), MinimumPeriod, MaximumPeriod);
            return MaximumPeriod - period;
        }

        internal int CalculatePulseVolume(ControlEvent control, ControlInstrument instrument)
        {
            // 時間経過による E の増減は C3 で接続する。C2 では初期値と共通制御 V を使う。
            double target = control.Volume * instrument.InitialVolume;
            int volume = (int)Math.Clamp(Math.Round(target, MidpointRounding.AwayFromZero), 0, MaximumVolume);
            if (Math.Abs(target - volume) > VolumeTolerance)
            {
                AddWarning(control, "VolumeQuantized", "Pulse の目標音量を 0〜15 の整数へ量子化しました。", target, volume);
            }
            return volume;
        }

        internal int CalculateWaveVolume(ControlEvent control, ControlInstrument instrument)
        {
            double target = control.Volume * instrument.OutputLevel / PercentScale;
            double volume;
            int register;
            // 境界を含めて小さい側を選び、同距離の tie を丸めモードへ依存させない。
            if (target <= QuarterVolume / MidpointDivisor)
            {
                volume = 0;
                register = 0;
            }
            else if (target <= (QuarterVolume + HalfVolume) / MidpointDivisor)
            {
                volume = QuarterVolume;
                register = QuarterVolumeRegister;
            }
            else if (target <= (HalfVolume + FullVolume) / MidpointDivisor)
            {
                volume = HalfVolume;
                register = HalfVolumeRegister;
            }
            else
            {
                volume = FullVolume;
                register = FullVolumeRegister;
            }
            if (target != volume)
            {
                AddWarning(control, "WaveVolumeQuantized", "Wave の目標音量を 0 / 25 / 50 / 100% へ量子化しました。", target, volume);
            }
            return register;
        }

        internal static byte CalculateRouting(double pan, byte channelMask)
        {
            if (pan < -PanThreshold)
            {
                return (byte)(channelMask & GameBoyRegisters.LeftRoutingMask);
            }
            if (pan > PanThreshold)
            {
                return (byte)(channelMask & GameBoyRegisters.RightRoutingMask);
            }
            return channelMask;
        }

        internal void ReportPan(ControlEvent control, double pan)
        {
            if (pan == LeftPan || pan == CenterPan || pan == RightPan)
            {
                return;
            }
            double converted = CenterPan;
            if (pan < -PanThreshold)
            {
                converted = LeftPan;
            }
            else if (pan > PanThreshold)
            {
                converted = RightPan;
            }
            AddWarning(control, "PanReduced", "連続パンを GB の左 / 両側 / 右 routing へ量子化しました。", pan, converted);
        }

        internal void ReportRetrigger(ControlEvent control)
        {
            ControlNote note = control.Note!;
            _report.AddWarning(new ConversionDiagnostic("EnvelopeRetriggered", "Pulse の継続音量変更で DAC を停止して再トリガーしました。DAC pop と実機の再起動副作用が生じます。")
            {
                SourceTrack = control.TrackIndex, SourceEvent = note.SourceEvent, SourceTick = note.Tick,
                OutputTrack = control.TrackIndex
            });
        }

        private void AddWarning(ControlEvent control, string code, string message, double original, double converted)
        {
            ControlNote note = control.Note!;
            _report.AddWarning(new ConversionDiagnostic(code, message)
            {
                SourceTrack = control.TrackIndex,
                SourceEvent = note.SourceEvent,
                SourceTick = note.Tick,
                OutputTrack = control.TrackIndex,
                Original = original.ToString("R", CultureInfo.InvariantCulture),
                Converted = converted.ToString("R", CultureInfo.InvariantCulture),
                MaximumError = Math.Abs(original - converted)
            });
        }
    }
}
