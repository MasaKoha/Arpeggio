using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx.Presets;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>保存定義の値だけを検証する。指紋不一致は拒否せず、生成もしない。</summary>
    internal static class SfxDefinitionValidator
    {
        private const int Sha256HexLength = 64;

        internal static void Validate(SfxDefinition? definition, ChipKind chip)
        {
            if (definition?.Known is not SfxDefinitionData data)
            {
                return;
            }
            try
            {
                SongValidator.Require(data.SchemaVersion == SfxDefinitionData.CurrentSchemaVersion
                    && data.GeneratorVersion == SfxDefinitionData.CurrentGeneratorVersion,
                    "未知版の sfx は JSON 全体として保持してください。");
                SfxParameterValidator.Validate(data.Parameters, chip);
                RequireHash(data.ParametersHash, "parametersHash");
                RequireHash(data.GeneratedHash, "generatedHash");
                SongValidator.Require(data.SourcePreset == null || IsPreset(data.SourcePreset),
                    "sfx.sourcePreset は正式プリセット名または null です。");
                if (data.LastRandomization != null)
                {
                    ValidateRandomization(data.LastRandomization, chip);
                }
            }
            catch (SfxParameterException exception)
            {
                throw new SongValidationException($"sfx.parameters.{exception.ParameterPath}: {exception.Message}", exception);
            }
        }

        private static void ValidateRandomization(SfxRandomization randomization, ChipKind chip)
        {
            SongValidator.Require(randomization.AlgorithmVersion == SfxRandomization.CurrentAlgorithmVersion,
                "未知の乱数版を持つ sfx は JSON 全体として保持してください。");
            if (randomization.Operation == SfxRandomizationOperation.Randomize)
            {
                SongValidator.Require(randomization.Category == "any" || IsPreset(randomization.Category),
                    "sfx.lastRandomization.category は正式カテゴリ名または any です。");
                SongValidator.Require(randomization.Strength == null && randomization.Locks == null
                    && randomization.BaseParametersHash == null, "randomize の適用外項目は null です。");
                return;
            }
            SongValidator.Require(randomization.Operation == SfxRandomizationOperation.Mutate,
                "sfx.lastRandomization.operation は randomize / mutate です。");
            SongValidator.Require(randomization.Category == null, "mutate の category は null です。");
            SongValidator.Require(randomization.Strength is double strength && SongValidator.IsInRange(strength, 0, 1),
                "mutate の strength は有限の0〜1です。");
            RequireHash(randomization.BaseParametersHash, "lastRandomization.baseParametersHash");
            ValidateLocks(randomization.Locks, chip);
        }

        private static void ValidateLocks(IReadOnlyList<string>? locks, ChipKind chip)
        {
            if (locks is null)
            {
                throw new SongValidationException("mutate の locks は正規パスの配列です。");
            }
            foreach (string path in locks)
            {
                if (path is null)
                {
                    throw new SongValidationException("sfx.lastRandomization.locks の要素は null にできません。");
                }
                try
                {
                    SfxParameterCatalog.Get(path, chip);
                }
                catch (SfxParameterException exception)
                {
                    throw new SongValidationException($"sfx.lastRandomization.locks: '{path}' は現在チップの正規パスではありません。", exception);
                }
            }
        }

        private static bool IsPreset(string? name)
        {
            foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
            {
                if (preset.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private static void RequireHash(string? hash, string path)
        {
            SongValidator.Require(IsHash(hash), $"sfx.{path} は小文字64桁の SHA-256 です。");
        }

        private static bool IsHash(string? hash)
        {
            if (hash is null || hash.Length != Sha256HexLength)
            {
                return false;
            }
            foreach (char digit in hash)
            {
                if (!(digit >= '0' && digit <= '9') && !(digit >= 'a' && digit <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
