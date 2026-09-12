using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.History;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;

namespace Arpeggio.Daw.Editing.Sfx
{
    /// <summary>新規候補と文書編集中の値を保持し、ジェスチャーの確定だけを履歴へ適用する。</summary>
    public sealed class SfxEditingModel
    {
        private readonly DawDocument document;
        private readonly SongHistory candidateHistory = new SongHistory();
        private Song current;
        private Song? gestureStart;
        private string documentRevision = string.Empty;

        /// <summary>現在文書とは独立した初期候補を作る。ファイルには書き込まない。</summary>
        public SfxEditingModel(DawDocument document)
        {
            this.document = document;
            current = CreatePreset(ChipKind.Nes, SfxPresetKind.Jump);
        }

        /// <summary>現在文書へまだ接続していない新規候補か。</summary>
        public bool IsNewCandidate { get; private set; } = true;
        /// <summary>確定前の一操作を保持しているか。</summary>
        public bool HasGesture => gestureStart is not null;
        /// <summary>候補入力の状態。</summary>
        public SfxEditingState State { get; private set; }
        /// <summary>訂正・再試行のために保持する最後の入力。</summary>
        public string InputPatch { get; private set; } = string.Empty;
        /// <summary>入力または確定が失敗した理由。</summary>
        public string Error { get; private set; } = string.Empty;
        /// <summary>不正入力に対応する正規パス。文書や保存全体のエラーでは空文字。</summary>
        public string ErrorPath { get; private set; } = string.Empty;
        /// <summary>表示の基準 revision から作業文書が変わったか。</summary>
        public bool HasDocumentChanged => !IsNewCandidate && SfxHash.ComputeRevision(document.Song) != documentRevision;
        /// <summary>最後の有効値のチップ。</summary>
        public ChipKind Chip => current.Chip;
        /// <summary>現在音の値と保存意図を区別した編集可否。</summary>
        public SfxSynchronizationState Synchronization => SfxSynchronization.Inspect(current);
        /// <summary>操作を戻せる数。新規候補では文書履歴を参照しない。</summary>
        public int UndoCount => IsNewCandidate ? candidateHistory.UndoCount : document.Session.History.UndoCount;
        /// <summary>操作をやり直せる数。</summary>
        public int RedoCount => IsNewCandidate ? candidateHistory.RedoCount : document.Session.History.RedoCount;

        /// <summary>表示・試聴側へ所有権を分離した最新の有効スナップショットを返す。</summary>
        public Song Snapshot() => Clone(current);

        /// <summary>読み込みや履歴復元後の文書を再生成せず取り込む。</summary>
        public void FollowDocument()
        {
            current = Clone(document.Song);
            documentRevision = SfxHash.ComputeRevision(current);
            IsNewCandidate = false;
            candidateHistory.Clear();
            ClearGesture();
        }

        /// <summary>指定チップの新規候補へ移る。候補間の変更は Undo できる。</summary>
        public bool NewCandidate(ChipKind chip, SfxPresetKind preset)
        {
            return StartCandidate(CreatePreset(chip, preset));
        }

        /// <summary>従来の音を保持し、SFX定義なしの新規候補にする。</summary>
        public bool NewLegacyCandidate(ChipKind chip, SfxPresetKind preset) =>
            StartCandidate(SfxPresetFactory.Create(chip, preset));

        private bool StartCandidate(Song candidate)
        {
            CancelGesture();
            if (!IsNewCandidate)
            {
                candidateHistory.Clear();
                current = candidate;
                IsNewCandidate = true;
                return true;
            }
            return CommitCandidate(candidate);
        }

        /// <summary>同じチップのプリセットを一操作として適用する。</summary>
        public bool SelectPreset(SfxPresetKind preset)
        {
            RequireEditable();
            Song candidate = CreatePreset(Chip, preset);
            candidate.Title = current.Title;
            return CommitCandidate(candidate);
        }

        /// <summary>開始値を一度だけ保持する。途中値は文書へ公開しない。</summary>
        public void BeginGesture()
        {
            RequireEditable();
            if (gestureStart is null)
            {
                gestureStart = Clone(current);
                State = SfxEditingState.Dragging;
            }
        }

        /// <summary>有効な途中値だけを候補へ反映し、不正入力は文字列と理由を保持する。</summary>
        public bool UpdatePatch(string patch)
        {
            if (!HasGesture)
            {
                BeginGesture();
            }
            InputPatch = patch;
            try
            {
                SfxDefinitionData definition = RequireEditable();
                SfxParameterPatchResult applied = SfxParameterPatch.Apply(definition.Parameters, Chip, patch);
                current = SfxEditor.CreateCandidate(applied.Parameters, Chip, current.Title,
                    definition.SourcePreset, definition.LastRandomization).Song;
                Error = string.Empty;
                ErrorPath = string.Empty;
                State = SfxEditingState.Dragging;
                return true;
            }
            catch (SfxParameterException exception)
            {
                Fail(SfxEditingState.Invalid, exception.Message, exception.ParameterPath);
                return false;
            }
        }

        /// <summary>有効な最終値を一回だけ保存・履歴へ確定する。失敗時は入力を維持する。</summary>
        public bool CommitGesture()
        {
            if (gestureStart is null || State == SfxEditingState.Invalid)
            {
                return false;
            }
            Song before = gestureStart;
            bool changed = Apply(before, current);
            if (!changed) { current = before; }
            ClearGesture();
            return changed;
        }

        /// <summary>文書・履歴を変更せず開始値へ戻す。</summary>
        public void CancelGesture()
        {
            if (gestureStart is not null)
            {
                current = gestureStart;
            }
            ClearGesture();
        }

        /// <summary>出自を含む乱数候補を一操作で確定し、同値なら何も記録しない。</summary>
        public bool ApplyRandomization(SfxParameterRandomizationResult result)
        {
            SfxDefinitionData definition = RequireEditable();
            if (!result.Changed)
            {
                return false;
            }
            Song candidate = SfxEditor.CreateCandidate(result.Parameters, Chip, current.Title,
                result.SourcePreset ?? definition.SourcePreset,
                result.Randomization ?? definition.LastRandomization).Song;
            return CommitCandidate(candidate);
        }

        /// <summary>候補または作業文書の履歴を一回戻す。</summary>
        public void Undo()
        {
            CancelGesture();
            if (UndoCount == 0)
            {
                return;
            }
            if (IsNewCandidate)
            {
                current = candidateHistory.Undo(current);
                return;
            }
            RequireDocumentUnchanged();
            document.Session.Undo();
            FollowDocument();
        }

        /// <summary>候補または作業文書の履歴を一回やり直す。</summary>
        public void Redo()
        {
            CancelGesture();
            if (RedoCount == 0)
            {
                return;
            }
            if (IsNewCandidate)
            {
                current = candidateHistory.Redo(current);
                return;
            }
            RequireDocumentUnchanged();
            document.Session.Redo();
            FollowDocument();
        }

        /// <summary>保存列を置換する前に、対象件数を現在 revision に対して取得する。</summary>
        public SfxEditResult InspectRegeneration()
        {
            RequireDocumentUnchanged();
            return document.Session.Sfx.Regenerate(true, documentRevision, dryRun: true);
        }

        /// <summary>確認した revision に限り保存パラメータから生成領域を置換する。</summary>
        public bool Regenerate(string expectedRevision)
        {
            RequireDocumentUnchanged();
            bool changed = document.Session.Sfx.Regenerate(true, expectedRevision).Changed;
            FollowDocument();
            return changed;
        }

        /// <summary>文書の定義だけを除き、通常編集へ移行する。</summary>
        public void Detach()
        {
            RequireDocumentUnchanged();
            document.Session.Sfx.Detach(documentRevision);
            FollowDocument();
        }

        /// <summary>外部境界の失敗を、入力を破棄せず表示状態へ反映する。</summary>
        public void Fail(SfxEditingState state, string message, string parameterPath = "")
        {
            State = state;
            Error = message;
            ErrorPath = parameterPath;
        }

        internal void ClearError()
        {
            State = HasGesture ? SfxEditingState.Dragging : SfxEditingState.None;
            Error = string.Empty;
            ErrorPath = string.Empty;
        }

        private bool CommitCandidate(Song candidate)
        {
            if (HasGesture)
            {
                throw new InvalidOperationException("現在の入力を確定または取り消してください。");
            }
            bool changed = Apply(current, candidate);
            if (changed) { current = candidate; }
            ClearGesture();
            return changed;
        }

        private bool Apply(Song before, Song candidate)
        {
            if (SfxHash.ComputeRevision(before) == SfxHash.ComputeRevision(candidate))
            {
                return false;
            }
            if (IsNewCandidate)
            {
                candidateHistory.Record(before);
                return true;
            }
            RequireDocumentUnchanged();
            SfxEditResult result = document.Session.Sfx.ApplyParameters(candidate.Sfx!.Known!, documentRevision);
            documentRevision = result.Revision;
            return result.Changed;
        }

        private void RequireDocumentUnchanged()
        {
            if (IsNewCandidate)
            {
                throw new InvalidOperationException("現在の文書を編集対象にしてください。");
            }
            if (document.HasExternalChange() || SfxHash.ComputeRevision(document.Song) != documentRevision)
            {
                Fail(SfxEditingState.Conflict, "文書が変更されています。保存または再読み込み後に再試行してください。");
                throw new SfxEditException("RevisionConflict", Error);
            }
        }

        private SfxDefinitionData RequireEditable()
        {
            SfxSynchronizationState synchronization = Synchronization;
            if (!synchronization.Editable)
            {
                throw new SfxEditException(synchronization.Reason.ToString(), "SFX を編集できません: " + synchronization.Reason);
            }
            return current.Sfx!.Known!;
        }

        private void ClearGesture()
        {
            gestureStart = null;
            State = SfxEditingState.None;
            InputPatch = string.Empty;
            Error = string.Empty;
            ErrorPath = string.Empty;
        }

        private static Song CreatePreset(ChipKind chip, SfxPresetKind preset)
        {
            SfxParameterPresetDescription description = SfxParameterPresetCatalog.Get(preset, chip);
            return SfxEditor.CreateCandidate(description.Parameters, chip, description.Name, description.Name).Song;
        }

        private static Song Clone(Song song) => SongSerializer.Deserialize(SongSerializer.Serialize(song));
    }
}
