using System;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Document
{
    /// <summary>チップ固有の音色とマクロの入力制約。</summary>
    internal static class InstrumentValidator
    {
        private const int MaximumVolume = 15;
        private const int WaveSampleCount = 32;
        private const int ShortLfsrWidth = 7;
        private const int LongLfsrWidth = 15;

        internal static void Validate(Instrument instrument, ChipKind chip)
        {
            if (instrument is null)
            {
                throw new SongValidationException("instrument は null にできません。");
            }
            SongValidator.Require(instrument.Id > 0, "音色 ID は正の整数です。");
            SongValidator.Require(instrument.Name != null, "音色名は null にできません。");
            SongValidator.Require(GetChip(instrument.Kind) == chip, "音色のチップ種別がソングと一致しません。");
            switch (instrument)
            {
                case NesPulseInstrument pulse:
                    ValidateDuty(pulse.Duty);
                    ValidateMacro(pulse.VolumeMacro, 0, MaximumVolume);
                    ValidateMacro(pulse.ArpeggioMacro);
                    ValidateMacro(pulse.PitchMacro);
                    ValidateMacro(pulse.DutyMacro, (int)DutyCycle.Percent12_5, (int)DutyCycle.Percent75);
                    break;
                case NesTriangleInstrument triangle:
                    ValidateMacro(triangle.ArpeggioMacro);
                    ValidateMacro(triangle.PitchMacro);
                    break;
                case NesNoiseInstrument noise:
                    SongValidator.Require(noise.NoiseMode != NoiseMode.None && Enum.IsDefined(typeof(NoiseMode), noise.NoiseMode), "NoiseMode が不正です。");
                    ValidateMacro(noise.VolumeMacro, 0, MaximumVolume);
                    ValidateMacro(noise.PitchMacro);
                    break;
                case NesDpcmInstrument:
                    break;
                case GbPulseInstrument pulse:
                    ValidateDuty(pulse.Duty);
                    SongValidator.Require(pulse.InitialVolume >= 0 && pulse.InitialVolume <= MaximumVolume, "初期音量は 0〜15 です。");
                    SongValidator.Require(pulse.EnvelopeStepFrames >= 0, "エンベロープ間隔は 0 以上です。");
                    ValidateMacro(pulse.VolumeMacro, 0, MaximumVolume);
                    ValidateMacro(pulse.ArpeggioMacro);
                    ValidateMacro(pulse.PitchMacro);
                    break;
                case GbWaveInstrument wave:
                    ValidateWave(wave);
                    ValidateMacro(wave.ArpeggioMacro);
                    ValidateMacro(wave.PitchMacro);
                    break;
                case GbNoiseInstrument noise:
                    SongValidator.Require(noise.LfsrWidth == ShortLfsrWidth || noise.LfsrWidth == LongLfsrWidth, "LFSR 幅は 7 または 15 です。");
                    ValidateMacro(noise.VolumeMacro, 0, MaximumVolume);
                    ValidateMacro(noise.PitchMacro);
                    break;
                case SnesSampleInstrument sample:
                    ValidateSample(sample);
                    break;
                default:
                    throw new SongValidationException("未対応の音色型です。");
            }
        }

        internal static ChannelKind GetChannel(InstrumentKind kind)
        {
            return kind switch
            {
                InstrumentKind.NesPulse or InstrumentKind.GbPulse => ChannelKind.Pulse,
                InstrumentKind.NesTriangle => ChannelKind.Triangle,
                InstrumentKind.NesNoise or InstrumentKind.GbNoise => ChannelKind.Noise,
                InstrumentKind.NesDpcm => ChannelKind.Dpcm,
                InstrumentKind.GbWave => ChannelKind.Wave,
                InstrumentKind.SnesSample => ChannelKind.Sample,
                _ => throw new SongValidationException("instrument.kind が不正です。")
            };
        }

        private static ChipKind GetChip(InstrumentKind kind)
        {
            return kind switch
            {
                InstrumentKind.NesPulse or InstrumentKind.NesTriangle or InstrumentKind.NesNoise or InstrumentKind.NesDpcm => ChipKind.Nes,
                InstrumentKind.GbPulse or InstrumentKind.GbWave or InstrumentKind.GbNoise => ChipKind.GameBoy,
                InstrumentKind.SnesSample => ChipKind.Snes,
                _ => throw new SongValidationException("instrument.kind が不正です。")
            };
        }

        private static void ValidateDuty(DutyCycle duty)
        {
            SongValidator.Require(duty != DutyCycle.None && Enum.IsDefined(typeof(DutyCycle), duty), "duty が不正です。");
        }

        private static void ValidateMacro(Macro? macro, int minimum = int.MinValue, int maximum = int.MaxValue)
        {
            if (macro is null)
            {
                return;
            }
            if (macro.Values is null)
            {
                throw new SongValidationException("マクロ values は配列です。");
            }
            SongValidator.Require(macro.LoopIndex >= -1 && macro.LoopIndex < macro.Values.Length, "マクロ loopIndex が範囲外です。");
            foreach (int value in macro.Values)
            {
                SongValidator.Require(value >= minimum && value <= maximum, "マクロの値が範囲外です。");
            }
        }

        private static void ValidateWave(GbWaveInstrument wave)
        {
            if (wave.Waveform is null || wave.Waveform.Length != WaveSampleCount)
            {
                throw new SongValidationException("GB 波形は 32 要素です。");
            }
            foreach (int sample in wave.Waveform)
            {
                SongValidator.Require(sample >= 0 && sample <= MaximumVolume, "GB 波形の各値は 0〜15 です。");
            }
            const int QuarterOutputPercent = 25;
            const int HalfOutputPercent = 50;
            const int FullOutputPercent = 100;
            SongValidator.Require(wave.OutputLevel == 0 || wave.OutputLevel == QuarterOutputPercent || wave.OutputLevel == HalfOutputPercent || wave.OutputLevel == FullOutputPercent, "GB 波形音量は 0・25・50・100 です。");
        }

        private static void ValidateSample(SnesSampleInstrument sample)
        {
            SongValidator.Require(sample.Waveform != SnesWaveformKind.None && Enum.IsDefined(typeof(SnesWaveformKind), sample.Waveform), "SNES 波形種別が不正です。");
            SongValidator.Require(SongValidator.IsInRange(sample.Pan, -1, 1), "音色 pan は -1〜1 です。");
            SongValidator.Require(SongValidator.IsInRange(sample.EchoSend, 0, 1), "エコー送り量は 0〜1 です。");
            AdsrEnvelope envelope = sample.Envelope;
            SongValidator.Require(SongValidator.IsInRange(envelope.AttackSeconds, 0, double.MaxValue), "attack は有限の非負秒数です。");
            SongValidator.Require(SongValidator.IsInRange(envelope.DecaySeconds, 0, double.MaxValue), "decay は有限の非負秒数です。");
            SongValidator.Require(SongValidator.IsInRange(envelope.ReleaseSeconds, 0, double.MaxValue), "release は有限の非負秒数です。");
            SongValidator.Require(SongValidator.IsInRange(envelope.SustainLevel, 0, 1), "sustain は 0〜1 です。");
            ValidateMacro(sample.ArpeggioMacro);
            ValidateMacro(sample.PitchMacro);
        }
    }
}
