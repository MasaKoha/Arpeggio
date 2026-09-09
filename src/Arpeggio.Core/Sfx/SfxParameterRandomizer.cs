using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>固定seedからカテゴリ生成または変異の候補を作る。保存や履歴の公開は行わない。</summary>
    public static class SfxParameterRandomizer
    {
        /// <summary>変異の既定強度。</summary>
        public const double DefaultStrength = 0.1;

        private const string AnyCategory = "any";

        /// <summary>現在値に依存しないカテゴリ生成。同値なら既存の出自・保存・履歴を維持する。</summary>
        public static SfxParameterRandomizationResult Randomize(SfxParameters current, ChipKind chip,
            string category, uint seed)
        {
            SfxParameters original = SfxParameterValidator.Normalize(current, chip);
            var random = new SfxRandomGenerator(seed);
            SfxParameterPresetDescription preset = SelectPreset(chip, category, random, out string canonicalCategory);
            var candidate = new SfxRandomizationCandidate(preset.Parameters, chip, Array.Empty<string>());
            SfxCategoryRandomization.Apply(candidate, chip, random);
            candidate.RepairEnvelopes();
            return Complete(original, candidate, chip, new SfxRandomization
            {
                Operation = SfxRandomizationOperation.Randomize, Seed = seed, Category = canonicalCategory
            }, preset.Name);
        }

        /// <summary>現在値へ強度に比例した変異を加える。ロックと無効レイヤーも抽選を消費する。</summary>
        public static SfxParameterRandomizationResult Mutate(SfxParameters current, ChipKind chip, uint seed,
            double strength = DefaultStrength, IReadOnlyList<string>? locks = null)
        {
            SfxParameters original = SfxParameterValidator.Normalize(current, chip);
            if (!double.IsFinite(strength) || strength < 0 || strength > 1)
            {
                throw SfxParameterException.Invalid("strength", "strength は有限の0〜1で指定してください。");
            }
            IReadOnlyList<string> canonicalLocks = ValidateLocks(locks, chip);
            var candidate = new SfxRandomizationCandidate(original, chip, canonicalLocks);
            SfxParameterMutation.Apply(candidate, chip, new SfxRandomGenerator(seed), strength);
            candidate.RepairEnvelopes();
            return Complete(original, candidate, chip, new SfxRandomization
            {
                Operation = SfxRandomizationOperation.Mutate, Seed = seed, Strength = strength,
                Locks = canonicalLocks, BaseParametersHash = SfxHash.ComputeParametersHash(original, chip)
            }, null);
        }

        private static SfxParameterPresetDescription SelectPreset(ChipKind chip, string category,
            SfxRandomGenerator random, out string canonicalCategory)
        {
            if (string.Equals(category?.Trim(), AnyCategory, StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<SfxParameterPresetDescription> presets = SfxParameterPresetCatalog.GetAll(chip);
                int selectedIndex = (int)Math.Floor(presets.Count * random.NextDouble());
                canonicalCategory = AnyCategory;
                return presets[selectedIndex];
            }
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(SfxParameterPresetCatalog.Parse(category), chip);
            canonicalCategory = preset.Name;
            return preset;
        }

        private static IReadOnlyList<string> ValidateLocks(IReadOnlyList<string>? locks, ChipKind chip)
        {
            var canonical = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in locks ?? Array.Empty<string>())
            {
                if (path == null)
                {
                    throw SfxParameterException.Invalid("locks", "ロックのパスはnullにできません。");
                }
                SfxParameterCatalog.Get(path, chip);
                if (seen.Add(path))
                {
                    canonical.Add(path);
                }
            }
            return canonical.AsReadOnly();
        }

        private static SfxParameterRandomizationResult Complete(SfxParameters original,
            SfxRandomizationCandidate candidate, ChipKind chip, SfxRandomization randomization, string? sourcePreset)
        {
            bool changed = candidate.Parameters != original;
            return new SfxParameterRandomizationResult
            {
                Parameters = changed ? candidate.Parameters : original,
                Changed = changed,
                SourcePreset = changed ? sourcePreset : null,
                Randomization = changed ? randomization : null,
                Changes = candidate.Compare(original),
                Warnings = SfxParameterValidator.Validate(candidate.Parameters, chip)
            };
        }
    }
}
