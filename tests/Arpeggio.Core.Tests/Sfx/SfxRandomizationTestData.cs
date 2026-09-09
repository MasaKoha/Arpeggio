using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>全変異項目を有効にし、境界から離した独立の入力値。</summary>
    internal static class SfxRandomizationTestData
    {
        internal static SfxParameters RichParameters(ChipKind chip)
        {
            var envelope = new SfxEnvelopeParameters
            {
                Volume = 10, AttackSeconds = 0.2, SustainSeconds = 0.4, DecaySeconds = 0.6, Punch = 0.5
            };
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(chip) with
            {
                Tone = new SfxToneParameters
                {
                    BaseFrequencyHz = 440, SlideSemitonesPerSecond = 12,
                    DeltaSlideSemitonesPerSecondSquared = 48, VibratoDepthCents = 100, VibratoSpeedHz = 10,
                    PitchChangeSemitones = 7, PitchChangeTimeSeconds = 0.2, RepeatPeriodSeconds = 0.3, Envelope = envelope
                },
                Noise = new SfxNoiseParameters { Enabled = true, Envelope = envelope }
            };
            return chip switch
            {
                ChipKind.Nes => parameters with { Nes = parameters.Nes! with
                {
                    DutyPercent = 50, DutySweepPercentPerSecond = 10, NoisePeriodIndex = 8, NoiseSlideIndicesPerSecond = 10
                } },
                ChipKind.GameBoy => parameters with { GameBoy = parameters.GameBoy! with
                {
                    DutyPercent = 50, DutySweepPercentPerSecond = 10, NoiseSelection = 64, NoiseSlideSelectionsPerSecond = 10
                } },
                _ => parameters with { Snes = parameters.Snes! with { NoiseRate = 16 } }
            };
        }

        internal static Song CompileWithDefinition(SfxParameters parameters, ChipKind chip,
            string? sourcePreset, SfxRandomization? randomization)
        {
            Song song = SfxSongCompiler.Compile(parameters, chip, "保存と再現").Song;
            song.Sfx = new SfxDefinition(new SfxDefinitionData
            {
                Parameters = parameters, ParametersHash = SfxHash.ComputeParametersHash(parameters, chip),
                SourcePreset = sourcePreset, LastRandomization = randomization,
                GeneratedHash = SfxHash.ComputeGeneratedHash(song)
            });
            return song;
        }
    }
}
