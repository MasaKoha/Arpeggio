using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.History;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Presets
{
    /// <summary>生成候補と既存保存形式・履歴スナップショット間の出自保持および再現を固定する。</summary>
    public sealed class SfxRandomizationProvenanceTests
    {
        /// <summary>三チップとseed端点でrandomizeを保存・復元し、完全パラメータと生成列を再現する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 0u)]
        [InlineData(ChipKind.Nes, 1u)]
        [InlineData(ChipKind.Nes, uint.MaxValue)]
        [InlineData(ChipKind.GameBoy, 0u)]
        [InlineData(ChipKind.GameBoy, 1u)]
        [InlineData(ChipKind.GameBoy, uint.MaxValue)]
        [InlineData(ChipKind.Snes, 0u)]
        [InlineData(ChipKind.Snes, 1u)]
        [InlineData(ChipKind.Snes, uint.MaxValue)]
        public void Randomize_SavedProvenanceReproducesCompleteRecipe(ChipKind chip, uint seed)
        {
            SfxParameters original = SfxParameterCatalog.CreateDefaults(chip);
            SfxParameterRandomizationResult result = SfxParameterRandomizer.Randomize(original, chip, "any", seed);
            Song song = SfxRandomizationTestData.CompileWithDefinition(result.Parameters, chip, result.SourcePreset, result.Randomization);
            string saved = SongSerializer.Serialize(song);
            Song restored = SongSerializer.Deserialize(saved);
            Assert.Equal(saved, SongSerializer.Serialize(restored));
            Assert.True(SfxSynchronization.Inspect(restored).Editable);
            SfxDefinitionData definition = restored.Sfx!.Known!;
            SfxRandomization provenance = definition.LastRandomization!;
            Assert.Equal(1, provenance.AlgorithmVersion);
            Assert.Equal(seed, provenance.Seed);
            Assert.Equal("any", provenance.Category);
            Assert.Null(provenance.Strength);
            Assert.Null(provenance.Locks);
            Assert.Null(provenance.BaseParametersHash);
            SfxParameterRandomizationResult replay = SfxParameterRandomizer.Randomize(original, chip, provenance.Category!, provenance.Seed);
            Assert.Equal(definition.Parameters, replay.Parameters);
            Assert.Equal(definition.SourcePreset, replay.SourcePreset);
            Song regenerated = SfxSongCompiler.Compile(replay.Parameters, chip).Song;
            Assert.Equal(definition.GeneratedHash, SfxHash.ComputeGeneratedHash(regenerated));
        }

        /// <summary>mutateの再現は保存した変更前の全値とhashを基点にし、Undo/Redoで出自も往復する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Mutate_ReplaysFromHistoryAndPreservesSourcePreset(ChipKind chip)
        {
            SfxParameterRandomizationResult randomized = SfxParameterRandomizer.Randomize(
                SfxParameterCatalog.CreateDefaults(chip), chip, "power-up", 0);
            Song before = SfxRandomizationTestData.CompileWithDefinition(randomized.Parameters, chip,
                randomized.SourcePreset, randomized.Randomization);
            SfxParameterRandomizationResult mutation = SfxParameterRandomizer.Mutate(randomized.Parameters, chip, uint.MaxValue, 0.1,
                new[] { "tone.baseFrequencyHz", "tone.envelope.punch" });
            Assert.Null(mutation.SourcePreset);
            Song after = SfxRandomizationTestData.CompileWithDefinition(mutation.Parameters, chip,
                before.Sfx!.Known!.SourcePreset, mutation.Randomization);
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(after));
            SfxDefinitionData definition = restored.Sfx!.Known!;
            SfxRandomization provenance = definition.LastRandomization!;
            Assert.Equal("powerup", definition.SourcePreset);
            Assert.Equal(SfxRandomizationOperation.Mutate, provenance.Operation);
            Assert.Equal(uint.MaxValue, provenance.Seed);
            Assert.Equal(0.1, provenance.Strength);
            Assert.Null(provenance.Category);
            Assert.Equal(mutation.Randomization!.Locks, provenance.Locks);
            Assert.Equal(SfxHash.ComputeParametersHash(randomized.Parameters, chip), provenance.BaseParametersHash);
            var history = new SongHistory();
            history.Record(before);
            Song undone = history.Undo(restored);
            Assert.Equal(SongSerializer.Serialize(before), SongSerializer.Serialize(undone));
            SfxParameterRandomizationResult replay = SfxParameterRandomizer.Mutate(undone.Sfx!.Known!.Parameters,
                chip, provenance.Seed, provenance.Strength!.Value, provenance.Locks);
            Assert.Equal(definition.Parameters, replay.Parameters);
            Assert.Equal(definition.GeneratedHash, SfxHash.ComputeGeneratedHash(SfxSongCompiler.Compile(replay.Parameters, chip).Song));
            Assert.Equal(SongSerializer.Serialize(restored), SongSerializer.Serialize(history.Redo(undone)));
            Assert.NotEqual(provenance.BaseParametersHash, SfxHash.ComputeParametersHash(SfxParameterCatalog.CreateDefaults(chip), chip));
        }

        /// <summary>最後の乱数記録は手動tweak後も保存でき、同値操作から出自更新を受け取らない。</summary>
        [Fact]
        public void Provenance_RemainsHistoricalAfterManualTweakAndNoOp()
        {
            const ChipKind Chip = ChipKind.Nes;
            SfxParameterRandomizationResult randomized = SfxParameterRandomizer.Randomize(
                SfxParameterCatalog.CreateDefaults(Chip), Chip, "pickup", 1);
            SfxParameters tweaked = SfxParameterPatch.Apply(randomized.Parameters, Chip,
                "{\"tone\":{\"baseFrequencyHz\":441}}").Parameters;
            Song song = SfxRandomizationTestData.CompileWithDefinition(tweaked, Chip, randomized.SourcePreset, randomized.Randomization);
            string saved = SongSerializer.Serialize(song);
            SfxDefinitionData restored = SongSerializer.Deserialize(saved).Sfx!.Known!;
            Assert.Equal(441, restored.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal("coin", restored.SourcePreset);
            Assert.Equal("coin", restored.LastRandomization!.Category);
            Assert.Equal(1u, restored.LastRandomization.Seed);
            SfxParameterRandomizationResult noOp = SfxParameterRandomizer.Mutate(restored.Parameters, Chip, 0, 0);
            Assert.False(noOp.Changed);
            Assert.Null(noOp.Randomization);
            Assert.Null(noOp.SourcePreset);
            Assert.Equal(saved, SongSerializer.Serialize(song));
        }
    }
}
