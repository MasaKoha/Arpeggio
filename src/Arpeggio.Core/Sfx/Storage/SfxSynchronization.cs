using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx.Storage
{
    /// <summary>保存済みの二つの指紋だけで同期状態を判定する。</summary>
    public static class SfxSynchronization
    {
        /// <summary>ソングを変更せず、直接編集の可否と全不一致理由を返す。</summary>
        public static SfxSynchronizationState Inspect(Song song)
        {
            SongValidator.Validate(song);
            if (song.Sfx is null)
            {
                return new SfxSynchronizationState(null, SfxEditabilityReason.MissingDefinition);
            }
            if (song.Sfx.Known is not SfxDefinitionData data)
            {
                return new SfxSynchronizationState(null, SfxEditabilityReason.UnsupportedSfxVersion);
            }
            var reasons = new List<SfxEditabilityReason>();
            string parametersHash = SfxHash.ComputeParametersHash(data.Parameters, song.Chip, data.SchemaVersion, data.GeneratorVersion);
            if (data.ParametersHash != parametersHash)
            {
                reasons.Add(SfxEditabilityReason.SavedParametersChanged);
            }
            if (data.GeneratedHash != SfxHash.ComputeGeneratedHash(song))
            {
                reasons.Add(SfxEditabilityReason.GeneratedContentChanged);
            }
            return new SfxSynchronizationState(data.Parameters, reasons.ToArray());
        }
    }
}
