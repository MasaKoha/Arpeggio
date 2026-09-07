using System.IO;
using Arpeggio.Daw.Presenters;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>画面境界を置き換えて、編集・保存・外部変更の状態遷移を検証する。</summary>
    public sealed class MainWindowPresenterTests
    {
        /// <summary>追加から削除までを履歴で戻せて、正本は明示保存まで変更しない。</summary>
        [Fact]
        public void AddMoveDeleteUndoRedoPreserveExplicitSaveBoundary()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            MainWindowPresenter presenter = fixture.Presenter;
            presenter.PianoRoll.Add(0, 60);
            Assert.True(presenter.IsDirty);
            Assert.Empty(SongSerializer.Load(fixture.Path).Tracks[0].Notes);

            presenter.PianoRoll.Press(1, 60, resizeToleranceTicks: 2, bypassSnap: false);
            presenter.PianoRoll.Drag(49, 64, bypassSnap: false);
            presenter.PianoRoll.EndDrag();
            Note moved = Assert.Single(fixture.Document.Song.Tracks[0].Notes);
            Assert.Equal(48, moved.Tick);
            Assert.Equal(64, moved.MidiNote);
            presenter.PianoRoll.Delete();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);

            presenter.Undo();
            Assert.Equal(48, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            presenter.Undo();
            Assert.Equal(0, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            presenter.Undo();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);
            Assert.False(presenter.IsDirty);
            presenter.Redo();
            presenter.Save();
            Assert.Single(SongSerializer.Load(fixture.Path).Tracks[0].Notes);
            Assert.False(presenter.IsDirty);
        }

        /// <summary>未編集なら外部保存を直ちに取り込み、再生中は出力を再開する。</summary>
        [Fact]
        public void CleanExternalChangeReloadsAndResumesPlayback()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.TogglePlayback();
            int startsBeforeReload = fixture.Audio.StartCount;
            fixture.AddExternalNote(96, 67);
            fixture.Presenter.ExternalFileChanged();

            Assert.Equal(96, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            Assert.False(fixture.Presenter.HasPendingExternalChange);
            Assert.False(fixture.Presenter.IsDirty);
            Assert.True(fixture.View.IsPlaying);
            Assert.Equal(startsBeforeReload + 1, fixture.Audio.StartCount);
        }

        /// <summary>未保存編集がある外部変更は保留し、R 相当の確認まで正本を上書きしない。</summary>
        [Fact]
        public void DirtyExternalChangeWaitsForConfirmationAndProtectsExternalFile()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.AddExternalNote(96, 67);
            fixture.Presenter.ExternalFileChanged();

            Assert.True(fixture.Presenter.HasPendingExternalChange);
            Assert.Contains("外部で変更されました。再読み込み（R）", fixture.View.Status);
            Assert.Equal(0, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            fixture.Presenter.Save();
            Assert.Equal(96, Assert.Single(SongSerializer.Load(fixture.Path).Tracks[0].Notes).Tick);

            fixture.Presenter.ConfirmReload();
            Assert.Equal(96, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            Assert.False(fixture.Presenter.HasPendingExternalChange);
            Assert.False(fixture.Presenter.IsDirty);
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>自己保存後の遅延通知は、その後の未保存編集と履歴を壊さない。</summary>
        [Fact]
        public void OwnSaveNotificationDoesNotReloadOrDiscardLaterEdit()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.Presenter.Save();
            fixture.Presenter.PianoRoll.Add(96, 64);
            int displaysBeforeNotification = fixture.View.SongDisplayCount;
            int historyBeforeNotification = fixture.Document.Session.History.UndoCount;
            fixture.Presenter.ExternalFileChanged();

            Assert.False(fixture.Presenter.HasPendingExternalChange);
            Assert.True(fixture.Presenter.IsDirty);
            Assert.Equal(2, fixture.Document.Song.Tracks[0].Notes.Count);
            Assert.Equal(displaysBeforeNotification, fixture.View.SongDisplayCount);
            Assert.Equal(historyBeforeNotification, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>不正な重なりはステータスに表示し、選択・履歴・ソングを維持する。</summary>
        [Fact]
        public void RejectedOverlapKeepsSongAndHistoryAndShowsError()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            string original = SongSerializer.Serialize(fixture.Document.Song);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            fixture.Presenter.Execute(() => fixture.Presenter.PianoRoll.Add(12, 64));

            Assert.Equal(original, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.Equal(0, fixture.Presenter.PianoRoll.SelectedTick);
            Assert.Contains("重", fixture.View.Status);
        }

        /// <summary>監視通知より先に保存しても、外部で更新された正本を上書きしない。</summary>
        [Fact]
        public void SaveDetectsExternalChangeBeforeWatcherNotification()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.AddExternalNote(96, 67);
            fixture.Presenter.Save();

            Assert.Equal(96, Assert.Single(SongSerializer.Load(fixture.Path).Tracks[0].Notes).Tick);
            Assert.Equal(0, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            Assert.True(fixture.Presenter.IsDirty);
            Assert.True(fixture.Presenter.HasPendingExternalChange);
        }

        /// <summary>不正な外部 JSON はエラーとして表示し、編集中のソング・履歴・再生を保持する。</summary>
        [Fact]
        public void InvalidExternalJsonKeepsEditingAndPlaybackState()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.Presenter.Transport.TogglePlayback();
            Song currentSong = fixture.Document.Song;
            string original = SongSerializer.Serialize(currentSong);
            int historyBeforeNotification = fixture.Document.Session.History.UndoCount;
            File.WriteAllText(fixture.Path, "{");
            fixture.Presenter.Execute(fixture.Presenter.ExternalFileChanged);

            Assert.Same(currentSong, fixture.Document.Song);
            Assert.Equal(original, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(historyBeforeNotification, fixture.Document.Session.History.UndoCount);
            Assert.True(fixture.Presenter.IsDirty);
            Assert.True(fixture.View.IsPlaying);
            Assert.True(fixture.Audio.IsRunning);
            Assert.Contains("JSON", fixture.View.Status);
        }
    }
}
