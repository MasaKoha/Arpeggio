using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Editing.Sfx;

namespace Arpeggio.Daw.Presenters.Sfx
{
    /// <summary>SFX の入力・一操作確定・保存と Open を分離し、View と試聴へ通知する。</summary>
    public sealed class SfxEditorPresenter : IDisposable
    {
        private const int KeyboardQuietMilliseconds = 150;
        private readonly DawDocument document;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private readonly Subject<Unit> changes = new Subject<Unit>();
        private readonly Subject<Unit> commits = new Subject<Unit>();
        private readonly Subject<Song> previewRequests = new Subject<Song>();
        private readonly Subject<Unit> previewStops = new Subject<Unit>();
        private readonly Subject<string> openRequests = new Subject<string>();
        private readonly Subject<Unit> inputResets = new Subject<Unit>();
        private readonly HashSet<string> locks = new HashSet<string>(StringComparer.Ordinal);
        private bool isDisposed;

        /// <summary>新規候補は現在文書と独立して作成し、購読を自身の寿命へ結び付ける。</summary>
        public SfxEditorPresenter(DawDocument document)
        {
            this.document = document;
            Model = new SfxEditingModel(document);
        }

        /// <summary>途中値・同期可否・履歴数を観測する編集モデル。</summary>
        public SfxEditingModel Model { get; }
        /// <summary>候補保存の成功状態。Open の失敗では失わない。</summary>
        public SfxCandidateFile CandidateFile { get; } = new SfxCandidateFile();
        /// <summary>表示内容が変わった通知。</summary>
        public IObservable<Unit> Changes => changes.AsObservable();
        /// <summary>一操作が変更を確定した通知。</summary>
        public IObservable<Unit> Commits => commits.AsObservable();
        /// <summary>確定済みまたは最後の有効値の試聴要求。</summary>
        public IObservable<Song> PreviewRequests => previewRequests.AsObservable();
        /// <summary>待機中の世代を含めた停止要求。</summary>
        public IObservable<Unit> PreviewStops => previewStops.AsObservable();
        /// <summary>保護検査を通った、保存済み候補を開く要求。</summary>
        public IObservable<string> OpenRequests => openRequests.AsObservable();
        /// <summary>操作確定時の自動試聴。読み込み・履歴復元では発火しない。</summary>
        public bool AutoPreview { get; set; } = true;
        /// <summary>最後に要求した乱数 seed。同じ seed の再生成に使う。</summary>
        public uint Seed { get; private set; }
        /// <summary>現在チップの仕様。数値範囲・単位を View に再定義しない。</summary>
        public IReadOnlyList<SfxParameterDescription> Parameters => SfxParameterCatalog.GetAll(Model.Chip);
        /// <summary>正規パスに展開済みの変異ロック。</summary>
        public IReadOnlyList<string> Locks => locks.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        /// <summary>最新候補の保存が完了しており、Open を要求できるか。</summary>
        public bool CanOpen => Model.IsNewCandidate && !Model.HasGesture && CandidateFile.IsCurrent(Model.Snapshot());
        /// <summary>不正入力を現在音と誤認させない再生ラベル。</summary>
        public string PlayLabel => Model.State == SfxEditingState.Invalid ? "最後の有効値を再生" : "再生";

        /// <summary>UI スレッドの入力と時計を接続し、最後のキー入力から150msで一操作へまとめる。</summary>
        public void BindKeyboard(IObservable<string> patches, IScheduler scheduler)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            subscriptions.Add(patches.Select(patch =>
            {
                UpdatePatch(patch);
                return Observable.Timer(TimeSpan.FromMilliseconds(KeyboardQuietMilliseconds), scheduler)
                    .TakeUntil(inputResets);
            }).Switch().Subscribe(_ => Commit()));
        }

        /// <summary>ドラッグ・数値入力の開始値を保持する。</summary>
        public void BeginGesture()
        {
            inputResets.OnNext(Unit.Default);
            Run(() => { Model.BeginGesture(); return false; });
        }
        /// <summary>途中値をメモリ内だけへ反映する。</summary>
        public void UpdatePatch(string patch) => Run(() => { Model.UpdatePatch(patch); return false; });
        /// <summary>有効な最終値を一履歴・一試聴へ確定する。</summary>
        public bool Commit()
        {
            inputResets.OnNext(Unit.Default);
            return Run(Model.CommitGesture, preview: true);
        }
        /// <summary>開始値へ戻し、保留中のキー確定と試聴を取り消す。</summary>
        public void Cancel()
        {
            Stop();
            Run(() => { Model.CancelGesture(); return false; });
        }
        /// <summary>指定チップの選択プリセットから新規候補を作り直す。</summary>
        public void NewCandidate(ChipKind chip, SfxPresetKind preset)
        {
            Stop();
            Run(() =>
            {
                bool changed = Model.NewCandidate(chip, preset);
                locks.RemoveWhere(path => !Parameters.Any(description => description.Path == path));
                return changed;
            }, preview: true);
        }
        /// <summary>従来の生成音を変更せず、新規候補として読み込む。</summary>
        public void NewLegacyCandidate(ChipKind chip, SfxPresetKind preset)
        {
            Stop();
            Run(() => Model.NewLegacyCandidate(chip, preset), preview: true);
        }
        /// <summary>現在チップのプリセットを一操作で適用する。</summary>
        public void SelectPreset(SfxPresetKind preset) => Run(() => Model.SelectPreset(preset), preview: true);
        /// <summary>新しい seed を一度発行し、カテゴリから候補を生成する。</summary>
        public void Randomize(string category) => Randomize(category, IssueSeed());
        /// <summary>明示 seed でカテゴリ生成を再現する。ロックは適用しない。</summary>
        public void Randomize(string category, uint seed)
        {
            Seed = seed;
            Run(() => Model.ApplyRandomization(SfxParameterRandomizer.Randomize(
                RequireParameters(), Model.Chip, category, seed)), preview: true);
        }
        /// <summary>新しい seed を一度発行し、ロック外の値を小さく変異する。</summary>
        public void Mutate(double strength = SfxParameterRandomizer.DefaultStrength) => Mutate(IssueSeed(), strength);
        /// <summary>明示 seed・強度・正規パスのロックで変異する。</summary>
        public void Mutate(uint seed, double strength)
        {
            Seed = seed;
            Run(() => Model.ApplyRandomization(SfxParameterRandomizer.Mutate(
                RequireParameters(), Model.Chip, seed, strength, Locks)), preview: true);
        }
        /// <summary>グループを正規パス集合へ展開して変異用ロックを変更する。</summary>
        public void SetGroupLock(string group, bool locked)
        {
            Run(() =>
            {
                string[] paths = Parameters.Where(description => description.Path == group ||
                    description.Path.StartsWith(group + ".", StringComparison.Ordinal))
                    .Select(description => description.Path).ToArray();
                if (paths.Length == 0)
                {
                    throw new ArgumentException("ロック対象のパラメータがありません。", nameof(group));
                }
                foreach (string path in paths)
                {
                    if (locked) { locks.Add(path); }
                    else { locks.Remove(path); }
                }
                return false;
            });
        }
        /// <summary>最後の有効値を現在文書と無関係に先頭から試聴する。</summary>
        public void Play()
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            previewRequests.OnNext(Model.Snapshot());
        }
        /// <summary>試聴と保留中の入力確定を無効化する。</summary>
        public void Stop()
        {
            if (isDisposed) { return; }
            inputResets.OnNext(Unit.Default);
            previewStops.OnNext(Unit.Default);
        }
        /// <summary>一操作戻す。復元された音は自動試聴しない。</summary>
        public void Undo() { Stop(); Run(() => { bool changed = Model.UndoCount > 0; Model.Undo(); return changed; }); }
        /// <summary>一操作やり直す。復元された音は自動試聴しない。</summary>
        public void Redo() { Stop(); Run(() => { bool changed = Model.RedoCount > 0; Model.Redo(); return changed; }); }
        /// <summary>読み込み・Undo・外部更新後の文書を音へ再生成せず表示する。</summary>
        public void FollowDocument()
        {
            Stop();
            Run(() =>
            {
                Model.FollowDocument();
                locks.Clear();
                Seed = Model.Snapshot().Sfx?.Known?.LastRandomization?.Seed ?? 0;
                return false;
            });
        }
        /// <summary>通常編集で文書の revision が変わった場合だけ、表示と試聴を無効化する。</summary>
        public void RefreshDocument()
        {
            if (Model.HasDocumentChanged) { FollowDocument(); }
        }
        /// <summary>外部変更時に入力と試聴を取り消し、競合を明示する。</summary>
        public void ExternalChange()
        {
            Stop();
            Run(() =>
            {
                Model.CancelGesture();
                Model.Fail(SfxEditingState.Conflict, "外部変更を再読み込みしてください。");
                return false;
            });
        }
        /// <summary>タブ離脱前に有効値を確定し、離脱後の試聴を残さない。</summary>
        public void LeaveTab() { Commit(); Stop(); }
        /// <summary>現在文書の保存や切替をせず、新規候補だけを新規保存する。</summary>
        public void SaveNew(string path)
        {
            Commit();
            Run(() =>
            {
                if (!Model.IsNewCandidate || Model.HasGesture)
                {
                    throw new InvalidOperationException("有効な新規候補を確定してから保存してください。");
                }
                CandidateFile.SaveNew(Model.Snapshot(), path);
                Model.ClearError();
                return false;
            });
        }
        /// <summary>最新候補の保存と現在文書の保護を確認してから、別操作として Open を要求する。</summary>
        public void OpenSaved()
        {
            Run(() =>
            {
                if (!CanOpen || document.IsDirty || document.HasExternalChange())
                {
                    throw new SfxEditException("RevisionConflict", "最新候補を保存し、現在文書の保存／再読み込みを完了してから開いてください。");
                }
                string path = CandidateFile.RequireCurrentPath(Model.Snapshot());
                Stop();
                openRequests.OnNext(path);
                Model.ClearError();
                return false;
            });
        }
        /// <summary>対象件数と revision を表示してから明示置換を要求する。</summary>
        public SfxEditResult InspectRegeneration() => Model.InspectRegeneration();
        /// <summary>事前確認した revision の生成領域だけを置換する。</summary>
        public void Regenerate(string expectedRevision) => Run(() => Model.Regenerate(expectedRevision), preview: true);
        /// <summary>生成列を維持して通常ソングへ移る。</summary>
        public void Detach() { Stop(); Run(() => { Model.Detach(); return true; }); }

        /// <summary>保留入力・試聴・全購読を終了する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            Stop();
            isDisposed = true;
            subscriptions.Dispose();
            Model.Fail(SfxEditingState.Disposed, string.Empty);
            changes.OnCompleted();
            commits.OnCompleted();
            previewRequests.OnCompleted();
            previewStops.OnCompleted();
            openRequests.OnCompleted();
            changes.Dispose();
            commits.Dispose();
            previewRequests.Dispose();
            previewStops.Dispose();
            openRequests.Dispose();
            inputResets.Dispose();
        }

        private bool Run(Func<bool> operation, bool preview = false)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            try
            {
                bool changed = operation();
                changes.OnNext(Unit.Default);
                if (changed)
                {
                    commits.OnNext(Unit.Default);
                    if (preview && AutoPreview) { Play(); }
                }
                return changed;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                IOException or UnauthorizedAccessException or SongValidationException)
            {
                SfxEditingState state = exception is SfxEditException { Code: "RevisionConflict" }
                    ? SfxEditingState.Conflict : SfxEditingState.Invalid;
                if (exception is IOException or UnauthorizedAccessException or SfxEditException { Code: "DestinationExists" }) { state = SfxEditingState.SaveFailed; }
                if (state == SfxEditingState.Conflict) { Stop(); }
                Model.Fail(state, exception.Message, (exception as SfxParameterException)?.ParameterPath ?? string.Empty);
                changes.OnNext(Unit.Default);
                return false;
            }
        }

        private SfxParameters RequireParameters() => Model.Synchronization.Parameters ??
            throw new InvalidOperationException("同期済みのパラメータ SFX を選択してください。");

        private static uint IssueSeed()
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt32(bytes);
        }
    }
}
