using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Daw.Editing.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Editing.Sfx
{
    /// <summary>編集中ファイルへの確定・競合・未知版・明示再生成を検証する。</summary>
    public sealed class SfxEditingModelTests
    {
        /// <summary>途中値を文書へ出さず、一確定と明示保存で定義を保持する。</summary>
        [Fact]
        public void DocumentCommitIsOneWorkingSaveAndExplicitSaveKeepsDefinition()
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            var presenter = fixture.Presenter.SfxEditor;
            byte[] original = File.ReadAllBytes(fixture.Path);
            presenter.BeginGesture();
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            Assert.False(fixture.Document.IsDirty);
            Assert.True(presenter.Commit());
            Assert.Equal(1, fixture.Document.Session.History.UndoCount);
            Assert.Equal(original, File.ReadAllBytes(fixture.Path));
            fixture.Presenter.Save();
            Assert.Equal(880, SongSerializer.Load(fixture.Path).Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
            fixture.Presenter.Undo();
            Assert.Equal(196, presenter.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
            fixture.Presenter.Redo();
            Assert.Equal(880, presenter.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
        }

        /// <summary>外部競合で途中値・履歴・外部ファイルを上書きしない。</summary>
        [Fact]
        public void ExternalConflictDoesNotApplyDraftOrOverwriteFile()
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            var presenter = fixture.Presenter.SfxEditor;
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            Song external = SongSerializer.Load(fixture.Path);
            external.Title = "external";
            SongSerializer.Save(external, fixture.Path);
            byte[] externalBytes = File.ReadAllBytes(fixture.Path);
            Assert.False(presenter.Commit());
            Assert.Equal(SfxEditingState.Conflict, presenter.Model.State);
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
            Assert.Equal(196, fixture.Document.Song.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(externalBytes, File.ReadAllBytes(fixture.Path));
            Assert.True(presenter.Model.HasGesture);
        }

        /// <summary>未知版は生成列を保持して編集を拒否し、detachのUndoで復元する。</summary>
        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("generatorVersion")]
        [InlineData("algorithmVersion")]
        public void UnknownVersionRemainsPlayableSnapshotAndRejectsEditing(string versionProperty)
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            if (versionProperty == "algorithmVersion")
            {
                fixture.Presenter.SfxEditor.AutoPreview = false;
                fixture.Presenter.SfxEditor.Mutate(1U, 1);
                fixture.Presenter.Save();
            }
            string content = File.ReadAllText(fixture.Path).Replace("\"" + versionProperty + "\": 1", "\"" + versionProperty + "\": 99", StringComparison.Ordinal);
            File.WriteAllText(fixture.Path, content);
            fixture.Presenter.Open(fixture.Path);
            var presenter = fixture.Presenter.SfxEditor;
            Assert.Equal(SfxEditabilityReason.UnsupportedSfxVersion, presenter.Model.Synchronization.Reason);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Throws<SfxEditException>(() => presenter.InspectRegeneration());
            presenter.Detach();
            Assert.Null(fixture.Document.Song.Sfx);
            fixture.Presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
        }

        /// <summary>両指紋の不一致を区別し、再生成をUndoできる。</summary>
        [Fact]
        public void GeneratedContentAndSavedParametersHaveDistinctStatesAndRegenerateIsUndoable()
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            fixture.Document.Session.Notes.Remove(0, 0);
            fixture.Presenter.Refresh();
            var presenter = fixture.Presenter.SfxEditor;
            Assert.Equal(SfxEditabilityReason.GeneratedContentChanged, presenter.Model.Synchronization.Reason);
            Assert.NotNull(presenter.Model.Synchronization.SavedParameters);
            SfxEditResult inspection = presenter.InspectRegeneration();
            presenter.Regenerate(inspection.Revision);
            Assert.True(presenter.Model.Synchronization.Editable);
            fixture.Presenter.Undo();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);
            Song modified = SongSerializer.Load(fixture.Path);
            SfxDefinitionData definition = modified.Sfx!.Known!;
            modified.Sfx = new SfxDefinition(definition with
            {
                Parameters = definition.Parameters with { Tone = definition.Parameters.Tone with { BaseFrequencyHz = 880 } }
            });
            SongSerializer.Save(modified, fixture.Path);
            fixture.Presenter.Open(fixture.Path);
            Assert.Equal(SfxEditabilityReason.SavedParametersChanged, presenter.Model.Synchronization.Reason);
        }

        /// <summary>保存とタブ離脱で有効なジェスチャーを一度だけ確定する。</summary>
        [Fact]
        public void SaveDuringGestureCommitsAndTabExitCommitsWithoutLeavingPreviewRequest()
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            var presenter = fixture.Presenter.SfxEditor;
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            fixture.Presenter.Save();
            Assert.False(presenter.Model.HasGesture);
            Assert.Equal(1, presenter.Model.UndoCount);
            Assert.Equal(880, SongSerializer.Load(fixture.Path).Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":440}}");
            presenter.LeaveTab();
            Assert.False(presenter.Model.HasGesture);
            Assert.Equal(2, presenter.Model.UndoCount);
        }

        /// <summary>出自付き変異を作業文書へ一履歴で適用し、Undoで変更前の全定義を戻す。</summary>
        [Fact]
        public void DocumentMutationPreservesLocksAndUndoRestoresDefinition()
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            var presenter = fixture.Presenter.SfxEditor;
            presenter.AutoPreview = false;
            string before = SongSerializer.Serialize(fixture.Document.Song);
            presenter.SetGroupLock("tone.envelope", true);
            presenter.Mutate(1U, 1);
            Assert.Equal(1, fixture.Document.Session.History.UndoCount);
            SfxDefinitionData definition = fixture.Document.Song.Sfx!.Known!;
            Assert.Equal(1U, definition.LastRandomization!.Seed);
            Assert.Equal(presenter.Locks, definition.LastRandomization.Locks);
            Assert.Equal("jump", definition.SourcePreset);
            fixture.Presenter.Undo();
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
        }

        /// <summary>作業保存のI/O失敗ではドラフトと履歴を保持し、復旧後に一回だけ確定する。</summary>
        [Fact]
        public void WorkingSaveFailureKeepsDraftForRetry()
        {
            using var fixture = new DawPresenterFixture();
            OpenSfx(fixture);
            var presenter = fixture.Presenter.SfxEditor;
            presenter.AutoPreview = false;
            string workingPath = fixture.Document.Session.Path!;
            string backupPath = workingPath + ".backup";
            presenter.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            File.Move(workingPath, backupPath);
            Directory.CreateDirectory(workingPath);
            try
            {
                Assert.False(presenter.Commit());
                Assert.Equal(SfxEditingState.SaveFailed, presenter.Model.State);
                Assert.True(presenter.Model.HasGesture);
                Assert.Equal(0, fixture.Document.Session.History.UndoCount);
                Assert.Equal(196, fixture.Document.Song.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
            }
            finally
            {
                Directory.Delete(workingPath);
                File.Move(backupPath, workingPath);
            }
            Assert.True(presenter.Commit());
            Assert.Equal(1, fixture.Document.Session.History.UndoCount);
            Assert.Equal(880, fixture.Document.Song.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
        }

        private static void OpenSfx(DawPresenterFixture fixture)
        {
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(SfxPresetKind.Jump, ChipKind.Nes);
            Song song = SfxEditor.CreateCandidate(preset.Parameters, ChipKind.Nes, "test", preset.Name).Song;
            SongSerializer.Save(song, fixture.Path);
            fixture.Presenter.Open(fixture.Path);
        }
    }
}
