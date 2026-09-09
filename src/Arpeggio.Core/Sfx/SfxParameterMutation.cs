using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>現在値に対する版1の二十二回の抽選順と対称変異幅。</summary>
    internal static class SfxParameterMutation
    {
        private const double SemitonesPerOctave = 12;
        private const double FrequencyWidthSemitones = 12;
        private const double DutyWidthPercent = 25;
        private const double DutySweepWidth = 25;
        private const double SmallNoiseWidth = 3;
        private const double GameBoyNoiseWidth = 16;
        private const double NesNoiseSlideWidth = 12;
        private const double GameBoyNoiseSlideWidth = 48;
        private static readonly (string Path, double Width)[] CommonRules =
        {
            ("tone.slideSemitonesPerSecond", 60),
            ("tone.deltaSlideSemitonesPerSecondSquared", 240),
            ("tone.vibratoDepthCents", 100),
            ("tone.vibratoSpeedHz", 5),
            ("tone.envelope.volume", 4),
            ("tone.envelope.attackSeconds", 0.1),
            ("tone.envelope.sustainSeconds", 0.2),
            ("tone.envelope.decaySeconds", 0.2),
            ("tone.envelope.punch", 0.25),
            ("tone.pitchChangeSemitones", 4),
            ("tone.pitchChangeTimeSeconds", 0.1),
            ("tone.repeatPeriodSeconds", 0.1),
            ("noise.envelope.volume", 4),
            ("noise.envelope.attackSeconds", 0.1),
            ("noise.envelope.sustainSeconds", 0.2),
            ("noise.envelope.decaySeconds", 0.2),
            ("noise.envelope.punch", 0.25)
        };

        internal static void Apply(SfxRandomizationCandidate candidate, ChipKind chip,
            SfxRandomGenerator random, double strength)
        {
            double frequencyOffset = DrawOffset(random, strength, FrequencyWidthSemitones);
            candidate.Set("tone.baseFrequencyHz", candidate.Read("tone.baseFrequencyHz")
                * Math.Pow(2, frequencyOffset / SemitonesPerOctave));
            foreach ((string path, double width) in CommonRules)
            {
                double offset = DrawOffset(random, strength, width);
                if (!KeepsZero(path, candidate.Read(path)))
                {
                    candidate.Set(path, candidate.Read(path) + offset);
                }
            }
            Offset(candidate, DutyPath(chip), DrawOffset(random, strength, DutyWidthPercent));
            Offset(candidate, DutySweepPath(chip), DrawOffset(random, strength, DutySweepWidth));
            double noiseWidth = chip == ChipKind.GameBoy ? GameBoyNoiseWidth : SmallNoiseWidth;
            Offset(candidate, NoiseSelectionPath(chip), DrawOffset(random, strength, noiseWidth));
            double noiseSlideWidth = chip == ChipKind.GameBoy ? GameBoyNoiseSlideWidth : NesNoiseSlideWidth;
            Offset(candidate, NoiseSlidePath(chip), DrawOffset(random, strength, noiseSlideWidth));
        }

        internal static string? DutyPath(ChipKind chip) => chip switch
        {
            ChipKind.Nes => "nes.dutyPercent",
            ChipKind.GameBoy => "gameBoy.dutyPercent",
            _ => null
        };

        internal static string NoiseSelectionPath(ChipKind chip) => chip switch
        {
            ChipKind.Nes => "nes.noisePeriodIndex",
            ChipKind.GameBoy => "gameBoy.noiseSelection",
            _ => "snes.noiseRate"
        };

        private static string? DutySweepPath(ChipKind chip) => chip switch
        {
            ChipKind.Nes => "nes.dutySweepPercentPerSecond",
            ChipKind.GameBoy => "gameBoy.dutySweepPercentPerSecond",
            _ => null
        };

        private static string? NoiseSlidePath(ChipKind chip) => chip switch
        {
            ChipKind.Nes => "nes.noiseSlideIndicesPerSecond",
            ChipKind.GameBoy => "gameBoy.noiseSlideSelectionsPerSecond",
            _ => null
        };

        private static double DrawOffset(SfxRandomGenerator random, double strength, double width)
            => (2 * random.NextDouble() - 1) * strength * width;

        private static bool KeepsZero(string path, double value)
            => value == 0 && (path == "tone.pitchChangeSemitones" || path == "tone.repeatPeriodSeconds");

        private static void Offset(SfxRandomizationCandidate candidate, string? path, double offset)
            => candidate.Set(path, candidate.Read(path) + offset);
    }
}
