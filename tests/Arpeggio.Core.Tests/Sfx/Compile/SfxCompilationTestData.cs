using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Tests.Sfx.Compile
{
    /// <summary>独立した固定包絡と、チップ別の生成マクロ参照をテストへ提供する。</summary>
    internal static class SfxCompilationTestData
    {
        internal static SfxParameters ShortParameters(ChipKind chip)
        {
            var envelope = new SfxEnvelopeParameters { Volume = 12, SustainSeconds = 0, DecaySeconds = 0.05 };
            return SfxParameterCatalog.CreateDefaults(chip) with
            {
                Tone = new SfxToneParameters { Envelope = envelope },
                Noise = new SfxNoiseParameters { Envelope = envelope }
            };
        }

        internal static Macro Volume(Instrument instrument) => instrument switch
        {
            NesPulseInstrument pulse => pulse.VolumeMacro!,
            GbPulseInstrument pulse => pulse.VolumeMacro!,
            NesNoiseInstrument noise => noise.VolumeMacro!,
            GbNoiseInstrument noise => noise.VolumeMacro!,
            SnesSampleInstrument sample => sample.VolumeMacro!,
            _ => throw new System.ArgumentException("未対応のテスト音色です。", nameof(instrument))
        };

        internal static Macro Pitch(Instrument instrument) => instrument switch
        {
            NesPulseInstrument pulse => pulse.PitchMacro!,
            GbPulseInstrument pulse => pulse.PitchMacro!,
            NesNoiseInstrument noise => noise.PitchMacro!,
            GbNoiseInstrument noise => noise.PitchMacro!,
            SnesSampleInstrument sample => sample.PitchMacro!,
            _ => throw new System.ArgumentException("未対応のテスト音色です。", nameof(instrument))
        };

        internal static Macro Duty(Instrument instrument) => instrument switch
        {
            NesPulseInstrument pulse => pulse.DutyMacro!,
            GbPulseInstrument pulse => pulse.DutyMacro!,
            _ => throw new System.ArgumentException("パルス音色が必要です。", nameof(instrument))
        };

        internal static SfxParameters WithDuty(SfxParameters parameters, ChipKind chip, double duty, double sweep)
        {
            return chip == ChipKind.Nes
                ? parameters with { Nes = parameters.Nes! with { DutyPercent = duty, DutySweepPercentPerSecond = sweep } }
                : parameters with { GameBoy = parameters.GameBoy! with { DutyPercent = duty, DutySweepPercentPerSecond = sweep } };
        }

        internal static SfxParameters WithNoise(SfxParameters parameters, ChipKind chip, int selection, double slide)
        {
            return chip == ChipKind.Nes
                ? parameters with { Nes = parameters.Nes! with { NoisePeriodIndex = selection, NoiseSlideIndicesPerSecond = slide } }
                : parameters with { GameBoy = parameters.GameBoy! with { NoiseSelection = selection, NoiseSlideSelectionsPerSecond = slide } };
        }
    }
}
