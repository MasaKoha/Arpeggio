using System;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Editing.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>文書から隔離した候補・ジェスチャー・履歴・保存と Open の境界を検証する。</summary>
    public sealed class SfxEditorPresenterTests
    {
        /// <summary>100回の途中更新は文書不変で、一確定だけを履歴と試聴へ公開する。</summary>
        [Fact]
        public void HundredUpdatesCommitOneHistoryAndOnePreviewWithoutChangingDocument()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.AutoPreview = false;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            byte[] fileBefore = File.ReadAllBytes(fixture.Path);
            int previews = 0;
            using IDisposable subscription = presenter.PreviewRequests.Subscribe(_ => previews++);
            presenter.AutoPreview = true;
            presenter.BeginGesture();
            for (int index = 0; index < 100; index++)
            {
                presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":" + (440 + index) + "}}");
            }
            Assert.Equal(0, presenter.Model.UndoCount);
            Assert.Equal(0, previews);
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.True(presenter.Commit());
            Assert.Equal(1, presenter.Model.UndoCount);
            Assert.Equal(1, previews);
            Assert.Equal(fileBefore, File.ReadAllBytes(fixture.Path));
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
            Assert.False(presenter.Commit());
            presenter.Undo();
            Assert.Equal(196, presenter.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
            Assert.Equal(1, previews);
            presenter.Redo();
            Assert.Equal(539, presenter.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
        }

        /// <summary>不正入力は最後の有効音を保持し、取消で開始値へ戻る。</summary>
        [Fact]
        public void InvalidInputKeepsLastValidSoundAndEscapeRestoresStart()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            string start = SongSerializer.Serialize(presenter.Model.Snapshot());
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":440}}");
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":\"bad\"}}");
            Assert.Equal(SfxEditingState.Invalid, presenter.Model.State);
            Assert.Contains("bad", presenter.Model.InputPatch);
            Assert.Equal("最後の有効値を再生", presenter.PlayLabel);
            Assert.Equal(440, presenter.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
            Assert.False(presenter.Commit());
            presenter.Cancel();
            Assert.Equal(start, SongSerializer.Serialize(presenter.Model.Snapshot()));
            Assert.Equal(0, presenter.Model.UndoCount);
        }

        /// <summary>偽時計で150ms集約と取消後の遅延確定の無効化を検証する。</summary>
        [Fact]
        public void KeyboardQuietPeriodUsesVirtualClockAndCancelInvalidatesPendingCommit()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            using var inputs = new Subject<string>();
            var scheduler = new HistoricalScheduler();
            presenter.BindKeyboard(inputs, scheduler);
            inputs.OnNext("{\"tone\":{\"baseFrequencyHz\":440}}");
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));
            inputs.OnNext("{\"tone\":{\"baseFrequencyHz\":880}}");
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(149));
            Assert.Equal(0, presenter.Model.UndoCount);
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, presenter.Model.UndoCount);
            inputs.OnNext("{\"tone\":{\"baseFrequencyHz\":220}}");
            presenter.Cancel();
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.Equal(1, presenter.Model.UndoCount);
            Assert.Equal(880, presenter.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
        }

        /// <summary>グループロックを正規パスで保存し、Undoで出自も戻す。</summary>
        [Fact]
        public void LocksAreCanonicalAndMutationUndoRestoresProvenance()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            presenter.SetGroupLock("tone.envelope", true);
            SfxEnvelopeParameters envelope = presenter.Model.Synchronization.Parameters!.Tone.Envelope;
            string before = SongSerializer.Serialize(presenter.Model.Snapshot());
            presenter.Mutate(1U, 1);
            Assert.Equal(envelope, presenter.Model.Synchronization.Parameters!.Tone.Envelope);
            SfxRandomization randomization = presenter.Model.Snapshot().Sfx!.Known!.LastRandomization!;
            Assert.Equal(1U, randomization.Seed);
            Assert.Equal(presenter.Locks, randomization.Locks);
            Assert.All(randomization.Locks!, path => Assert.StartsWith("tone.envelope.", path));
            presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(presenter.Model.Snapshot()));
            presenter.Mutate(2U, 0);
            Assert.Equal(0, presenter.Model.UndoCount);
            Assert.Equal(1, presenter.Model.RedoCount);
        }

        /// <summary>保存成功をOpen拒否と分離し、古い候補を開けない。</summary>
        [Fact]
        public void SaveSucceedsBeforeOpenRefusalAndStaleCandidateCannotOpen()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            fixture.Presenter.PianoRoll.Add(0, 60);
            presenter.NewCandidate(ChipKind.GameBoy, SfxPresetKind.Coin);
            string path = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "candidate.json");
            presenter.SaveNew(path);
            Assert.True(File.Exists(path));
            presenter.OpenSaved();
            Assert.Equal(fixture.Path, fixture.Document.Path);
            Assert.Equal(path, presenter.CandidateFile.SavedPath);
            Assert.True(presenter.CanOpen);
            fixture.Presenter.Save();
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":440}}");
            presenter.Commit();
            Assert.False(presenter.CanOpen);
            presenter.OpenSaved();
            Assert.Equal(fixture.Path, fixture.Document.Path);
            presenter.Undo();
            presenter.OpenSaved();
            Assert.Equal(path, fixture.Document.Path);
        }

        /// <summary>従来の雛形は生成音と定義なしの形式を維持して候補へ入り、Undoで戻せる。</summary>
        [Fact]
        public void LegacyCandidateKeepsFactoryBytesAndIndependentHistory()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.AutoPreview = false;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            string before = SongSerializer.Serialize(presenter.Model.Snapshot());
            string documentBefore = SongSerializer.Serialize(fixture.Document.Song);
            presenter.NewLegacyCandidate(ChipKind.GameBoy, SfxPresetKind.Coin);
            Assert.Equal(SongSerializer.Serialize(SfxPresetFactory.Create(ChipKind.GameBoy, SfxPresetKind.Coin)),
                SongSerializer.Serialize(presenter.Model.Snapshot()));
            Assert.False(presenter.Model.Synchronization.Editable);
            Assert.Equal(documentBefore, SongSerializer.Serialize(fixture.Document.Song));
            presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(presenter.Model.Snapshot()));
        }

        /// <summary>チップ切替をUndoでき、保存失敗で成功済みのパスを失わない。</summary>
        [Fact]
        public void ChipChangeIsUndoableAndSaveFailureKeepsPreviousSavedPath()
        {
            using var fixture = new DawPresenterFixture();
            var presenter = fixture.Presenter.SfxEditor;
            presenter.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            presenter.NewCandidate(ChipKind.Snes, SfxPresetKind.Jump);
            Assert.Equal(ChipKind.Snes, presenter.Model.Chip);
            presenter.Undo();
            Assert.Equal(ChipKind.Nes, presenter.Model.Chip);
            string path = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "candidate.json");
            presenter.SaveNew(path);
            byte[] original = File.ReadAllBytes(path);
            presenter.SaveNew(path);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(path, presenter.CandidateFile.SavedPath);
            Assert.Equal(SfxEditingState.SaveFailed, presenter.Model.State);
            presenter.SaveNew(Path.Combine(Path.GetDirectoryName(path)!, "retry.json"));
            Assert.Equal(SfxEditingState.None, presenter.Model.State);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
    }
}
