using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Core.Session
{
    /// <summary>効果音の定義と生成領域を候補上で完成させ、一回のセッション編集へ適用する。</summary>
    public sealed class SfxEditor
    {
        private readonly EditSession _session;

        /// <summary>既存の保存・履歴境界を持つ編集セッションを指定する。</summary>
        public SfxEditor(EditSession session)
        {
            _session = session;
        }

        /// <summary>新規の定義付き候補を純粋生成する。保存・セッション切替は行わない。</summary>
        public static SfxSongCompilationResult CreateCandidate(SfxParameters parameters, ChipKind chip,
            string title = "", string? sourcePreset = null, SfxRandomization? lastRandomization = null)
        {
            SfxSongCompilationResult generation = SfxSongCompiler.Compile(parameters, chip, title);
            Song candidate = generation.Song;
            candidate.Sfx = new SfxDefinition(new SfxDefinitionData
            {
                Parameters = generation.Curves.Parameters,
                ParametersHash = SfxHash.ComputeParametersHash(generation.Curves.Parameters, chip),
                SourcePreset = sourcePreset,
                LastRandomization = lastRandomization,
                GeneratedHash = SfxHash.ComputeGeneratedHash(candidate)
            });
            // 出自の locks が呼び出し元の可変リストでも、保存する定義の所有権を切り離す。
            candidate.Sfx = Clone(candidate).Sfx;
            return generation;
        }

        /// <summary>同期済み定義へ部分 patch を一括適用する。同値なら保存と履歴を増やさない。</summary>
        public SfxEditResult Tweak(string patch, string? expectedRevision = null, bool dryRun = false)
        {
            Song current = ReadCurrent(expectedRevision);
            SfxSynchronizationState synchronization = SfxSynchronization.Inspect(current);
            if (!synchronization.Editable)
            {
                throw Uneditable(synchronization.Reason);
            }
            SfxDefinitionData definition = RequireDefinition(current);
            SfxParameterPatchResult applied = SfxParameterPatch.Apply(definition.Parameters, current.Chip, patch);
            SfxSongCompilationResult generation = CreateCandidate(applied.Parameters, current.Chip,
                current.Title, definition.SourcePreset, definition.LastRandomization);
            // 同値 patch では保存済み指紋や出自を正規化し直して書き換えない。
            Song candidate = applied.Changed ? generation.Song : Clone(current);
            return Complete("tweak", current, candidate, dryRun, generation);
        }

        /// <summary>明示要求に限り保存パラメータから生成領域を全置換する。未知版は拒否する。</summary>
        public SfxEditResult Regenerate(bool replaceGenerated, string? expectedRevision = null, bool dryRun = false)
        {
            Song current = ReadCurrent(expectedRevision);
            if (!replaceGenerated)
            {
                throw SfxParameterException.Invalid("replaceGenerated", "生成領域を置換する明示要求が必要です。");
            }
            SfxDefinitionData definition = RequireDefinition(current);
            SfxSongCompilationResult generation = CreateCandidate(definition.Parameters, current.Chip,
                current.Title, definition.SourcePreset, definition.LastRandomization);
            return Complete("regenerate", current, generation.Song, dryRun, generation,
                new SfxReplacementSummary(current));
        }

        /// <summary>既知版・未知版とも定義だけを除去し、生成列をそのまま通常ソングとして保存する。</summary>
        public SfxEditResult Detach(string? expectedRevision = null, bool dryRun = false)
        {
            Song current = ReadCurrent(expectedRevision);
            if (current.Sfx is null)
            {
                throw Uneditable(SfxEditabilityReason.MissingDefinition);
            }
            Song candidate = Clone(current);
            candidate.Sfx = null;
            return Complete("detach", current, candidate, dryRun);
        }

        /// <summary>確定した全パラメータと出自を、同期済み文書へ一履歴で適用する。</summary>
        public SfxEditResult ApplyParameters(SfxDefinitionData definition, string expectedRevision)
        {
            Song current = ReadCurrent(expectedRevision);
            SfxSynchronizationState synchronization = SfxSynchronization.Inspect(current);
            if (!synchronization.Editable)
            {
                throw Uneditable(synchronization.Reason);
            }
            SfxSongCompilationResult generation = CreateCandidate(definition.Parameters, current.Chip,
                current.Title, definition.SourcePreset, definition.LastRandomization);
            bool changed = generation.Curves.Parameters != RequireDefinition(current).Parameters;
            return Complete("parameters", current, changed ? generation.Song : Clone(current), false, generation);
        }

        private Song ReadCurrent(string? expectedRevision)
        {
            Song current = Clone(_session.GetSong());
            if (expectedRevision is not null)
            {
                RequireRevision(current, expectedRevision);
            }
            return current;
        }

        private SfxEditResult Complete(string operation, Song current, Song candidate, bool dryRun,
            SfxSongCompilationResult? generation = null, SfxReplacementSummary? replacement = null)
        {
            string revision = SfxHash.ComputeRevision(current);
            string candidateRevision = SfxHash.ComputeRevision(candidate);
            bool changed = revision != candidateRevision;
            var result = new SfxEditResult(operation, changed, dryRun,
                changed && !dryRun ? candidateRevision : revision, candidate, generation, replacement);
            if (!changed || dryRun)
            {
                return result;
            }
            _session.Change(target =>
            {
                RequireRevision(target, revision);
                // 差分公開による音色参照の再利用で、返却する生成曲線と Song の対応を変えない。
                SongSnapshotPublisher.Apply(target, Clone(candidate));
            }, () =>
            {
                RequireRevision(_session.GetSong(), revision);
                // 外部編集を知らないまま古い生成列で上書きしない。CLI でも保存直前に照合する。
                RequireRevision(SongSerializer.Load(_session.Path!), revision);
            });
            return result;
        }

        private static SfxDefinitionData RequireDefinition(Song song)
        {
            if (song.Sfx is null)
            {
                throw Uneditable(SfxEditabilityReason.MissingDefinition);
            }
            return song.Sfx.Known ?? throw Uneditable(SfxEditabilityReason.UnsupportedSfxVersion);
        }

        private static SfxEditException Uneditable(SfxEditabilityReason reason)
        {
            return new SfxEditException(reason.ToString(), $"SFX を編集できません: {reason}。");
        }

        private static void RequireRevision(Song song, string expectedRevision)
        {
            if (SfxHash.ComputeRevision(song) != expectedRevision)
            {
                throw new SfxEditException("RevisionConflict", "ソングが変更されています。現在の状態を読み直してください。");
            }
        }

        private static Song Clone(Song song)
        {
            return SongSerializer.Deserialize(SongSerializer.Serialize(song));
        }
    }
}
