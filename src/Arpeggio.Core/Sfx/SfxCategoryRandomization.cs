using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>カテゴリ初期値に対する版1の十五回の抽選順と変更幅。</summary>
    internal static class SfxCategoryRandomization
    {
        private const double FrequencySemitoneSpan = 12;
        private const double FrequencySemitoneOffset = 6;
        private const double SemitonesPerOctave = 12;
        private const double MinimumScale = 0.5;
        private const int VibratoDepthRange = 50;
        private const int MinimumVibratoSpeed = 3;
        private const int VibratoSpeedRange = 6;
        private const int MinimumVolume = 10;
        private const int VolumeChoices = 4;
        private const int SmallNoiseChoices = 7;
        private const int SmallNoiseOffset = 3;
        private const int GameBoyNoiseChoices = 17;
        private const int GameBoyNoiseOffset = 8;

        internal static void Apply(SfxRandomizationCandidate candidate, ChipKind chip, SfxRandomGenerator random)
        {
            candidate.Set("tone.baseFrequencyHz", candidate.Read("tone.baseFrequencyHz")
                * Math.Pow(2, (FrequencySemitoneSpan * random.NextDouble() - FrequencySemitoneOffset) / SemitonesPerOctave));
            Scale(candidate, "tone.envelope.attackSeconds", random);
            Scale(candidate, "tone.envelope.sustainSeconds", random);
            Scale(candidate, "tone.envelope.decaySeconds", random);
            Scale(candidate, "tone.slideSemitonesPerSecond", random);
            Scale(candidate, "tone.deltaSlideSemitonesPerSecondSquared", random);
            candidate.Set("tone.vibratoDepthCents", Math.Round(VibratoDepthRange * random.NextDouble(), MidpointRounding.AwayFromZero));
            candidate.Set("tone.vibratoSpeedHz", MinimumVibratoSpeed + VibratoSpeedRange * random.NextDouble());
            Volume(candidate, "tone.envelope.volume", random);
            Scale(candidate, "noise.envelope.attackSeconds", random);
            Scale(candidate, "noise.envelope.sustainSeconds", random);
            Scale(candidate, "noise.envelope.decaySeconds", random);
            Volume(candidate, "noise.envelope.volume", random);
            SetDuty(candidate, chip, random.NextDouble());
            string noisePath = SfxParameterMutation.NoiseSelectionPath(chip);
            int choiceCount = chip == ChipKind.GameBoy ? GameBoyNoiseChoices : SmallNoiseChoices;
            int offset = chip == ChipKind.GameBoy ? GameBoyNoiseOffset : SmallNoiseOffset;
            candidate.Set(noisePath, candidate.Read(noisePath) + Math.Floor(choiceCount * random.NextDouble()) - offset);
        }

        private static void Scale(SfxRandomizationCandidate candidate, string path, SfxRandomGenerator random)
            => candidate.Set(path, candidate.Read(path) * (MinimumScale + random.NextDouble()));

        private static void Volume(SfxRandomizationCandidate candidate, string path, SfxRandomGenerator random)
            => candidate.Set(path, MinimumVolume + Math.Floor(VolumeChoices * random.NextDouble()));

        private static void SetDuty(SfxRandomizationCandidate candidate, ChipKind chip, double uniform)
        {
            string? path = SfxParameterMutation.DutyPath(chip);
            if (path == null)
            {
                return;
            }
            SfxParameterDescription description = SfxParameterCatalog.Get(path, chip);
            int choiceIndex = (int)Math.Floor(description.Choices.Count * uniform);
            candidate.Set(path, (double)description.Choices[choiceIndex]);
        }
    }
}
