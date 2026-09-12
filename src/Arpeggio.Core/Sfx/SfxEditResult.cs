using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Core.Sfx
{
    /// <summary>編集結果と独立した候補。dry-run の現状態と予定状態を区別する。</summary>
    public sealed class SfxEditResult
    {
        internal SfxEditResult(string operation, bool changed, bool dryRun, string revision,
            Song candidate, SfxSongCompilationResult? generation, SfxReplacementSummary? replacement,
            IReadOnlyList<SfxParameterChange>? changes = null)
        {
            Operation = operation;
            Changed = changed;
            DryRun = dryRun;
            Revision = revision;
            Candidate = candidate;
            CandidateRevision = SfxHash.ComputeRevision(candidate);
            Synchronization = SfxSynchronization.Inspect(candidate);
            Generation = generation;
            Replacement = replacement;
            Changes = changes ?? Array.Empty<SfxParameterChange>();
        }

        /// <summary>tweak / randomize / mutate / regenerate / detach の操作名。</summary>
        public string Operation { get; }

        /// <summary>候補が操作前と異なるか。dry-run では変更予定を表す。</summary>
        public bool Changed { get; }

        /// <summary>保存・履歴・公開を行わない予行か。</summary>
        public bool DryRun { get; }

        /// <summary>成功後の現状態の revision。dry-run では操作前の値。</summary>
        public string Revision { get; }

        /// <summary>候補全体の revision。同値操作では現状態と等しい。</summary>
        public string CandidateRevision { get; }

        /// <summary>公開ソング・履歴と可変状態を共有しない、適用予定または適用済みの候補。</summary>
        public Song Candidate { get; }

        /// <summary>候補の編集可否と現在パラメータ／保存意図の区別。</summary>
        public SfxSynchronizationState Synchronization { get; }

        /// <summary>生成した曲線と診断。detach は生成しないため null。</summary>
        public SfxSongCompilationResult? Generation { get; }

        /// <summary>regenerate の全置換対象件数。他の操作は null。</summary>
        public SfxReplacementSummary? Replacement { get; }

        /// <summary>乱数操作の値変更と包絡補正。他の操作では空。</summary>
        public IReadOnlyList<SfxParameterChange> Changes { get; }
    }
}
