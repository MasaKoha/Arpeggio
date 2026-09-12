using System.Linq;
using Arpeggio.Core.Document;
using Xunit;
using Arpeggio.Daw.Presenters.PianoRoll;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>削除軌跡・音量・グリッドとキー操作相当の複数編集を検証する。</summary>
    public sealed class PianoRollEditingTests
    {
        /// <summary>離れた入力通知間の線分上の音も消し、ドラッグ全体を一履歴にする。</summary>
        [Fact]
        public void PaintEraseSweepsWholeSegmentInOneHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Add(48, 64);
            roll.Add(96, 68);
            roll.Add(144, 64);
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.BeginErase(1, 60);
            Assert.Equal(3, fixture.Document.Song.Tracks[0].Notes.Count);
            roll.Drag(121, 70, false);
            roll.Drag(1, 60, false);
            roll.EndDrag();
            Assert.Equal(144, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            Assert.Equal(1, roll.Selection.Count);
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(4, fixture.Document.Song.Tracks[0].Notes.Count);
            fixture.Presenter.Redo();
            Assert.Single(fixture.Document.Song.Tracks[0].Notes);
        }

        /// <summary>削除軌跡の外接矩形内でも線分から離れた音は消さない。</summary>
        [Fact]
        public void PaintEraseDoesNotDeleteOutsideActualPathOrGhostTrack()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(48, 60);
            roll.SelectTrack(1);
            roll.Add(48, 65);
            roll.SelectTrack(0);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.BeginErase(0, 60);
            roll.Drag(100, 70, false);
            roll.EndDrag();
            Assert.Single(fixture.Document.Song.Tracks[0].Notes);
            Assert.Single(fixture.Document.Song.Tracks[1].Notes);
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>全選択は選択トラックだけに効き、移調・グリッド移動・音量を全件変更する。</summary>
        [Fact]
        public void SelectAllAndKeyboardEditsAffectOnlySelectedTrack()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(48, 60);
            roll.Add(96, 64);
            roll.SelectTrack(1);
            roll.Add(48, 72);
            roll.SelectTrack(0);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.SelectAll();
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.Contains("2 音選択", fixture.View.Status);
            roll.MoveSelection(0, 1);
            Assert.Equal(new[] { 61, 65 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.MidiNote));
            roll.MoveSelection(0, -1);
            roll.MoveSelection(0, 12);
            Assert.Equal(new[] { 72, 76 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.MidiNote));
            roll.MoveSelection(0, -12);
            roll.MoveSelection(roll.SnapTicks, 0);
            Assert.Equal(new[] { 60, 108 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
            roll.MoveSelection(-roll.SnapTicks, 0);
            roll.ChangeVolume(-1);
            Assert.All(fixture.Document.Song.Tracks[0].Notes, note => Assert.Equal(14, note.Volume));
            roll.ChangeVolume(1);
            Assert.All(fixture.Document.Song.Tracks[0].Notes, note => Assert.Equal(15, note.Volume));
            Note ghost = Assert.Single(fixture.Document.Song.Tracks[1].Notes);
            Assert.Equal(48, ghost.Tick);
            Assert.Equal(72, ghost.MidiNote);
            Assert.Equal(15, ghost.Volume);
            roll.Delete();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);
            Assert.Single(fixture.Document.Song.Tracks[1].Notes);
            fixture.Presenter.Undo();
            Assert.Equal(2, fixture.Document.Song.Tracks[0].Notes.Count);
        }

        /// <summary>すべてのスナップ単位と一時解除を固定 tick へ変換する。</summary>
        [Theory]
        [InlineData(SnapResolution.Bar, 192)]
        [InlineData(SnapResolution.Half, 96)]
        [InlineData(SnapResolution.Quarter, 48)]
        [InlineData(SnapResolution.Eighth, 24)]
        [InlineData(SnapResolution.Sixteenth, 12)]
        [InlineData(SnapResolution.Triplet, 16)]
        [InlineData(SnapResolution.None, 1)]
        public void SnapSelectionControlsAdditionAndKeyboardStep(SnapResolution resolution, int expectedTicks)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            Assert.Equal(SnapResolution.Sixteenth, roll.Resolution);
            roll.SetSnapResolution(resolution);
            Assert.Equal(expectedTicks, roll.SnapTicks);
            roll.Add(expectedTicks * 1.2, 60);
            Assert.Equal(expectedTicks, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            roll.MoveSelection(roll.SnapTicks, 0);
            Assert.Equal(expectedTicks * 2, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            Assert.Equal(7, SnapGrid.Snap(7, resolution, true));
            Assert.Equal(resolution, roll.Resolution);
        }

        /// <summary>音量レーンの途中通知で複数音を更新し、一履歴から Undo/Redo できる。</summary>
        [Fact]
        public void VelocityDragChangesAllSelectedNotesInOneHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.ChangeVolume(-3);
            roll.Add(48, 64);
            roll.ChangeVolume(-11);
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            Note[] original = fixture.Document.Song.Tracks[0].Notes.ToArray();
            roll.PressVelocity(0, 12, 2);
            roll.Velocity.Drag(9);
            Assert.Equal(new[] { 9, 1 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Volume));
            roll.Velocity.Drag(0);
            Assert.All(fixture.Document.Song.Tracks[0].Notes, note => Assert.Equal(0, note.Volume));
            roll.Velocity.Drag(15);
            roll.EndDrag();
            Assert.Equal(new[] { 15, 7 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Volume));
            Assert.Equal(new[] { 12, 4 }, original.Select(note => note.Volume));
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 12, 4 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Volume));
            fixture.Presenter.Redo();
            Assert.Equal(new[] { 15, 7 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Volume));
        }

        /// <summary>選択外の棒を掴んだ場合はその音だけ選択して編集する。</summary>
        [Fact]
        public void VelocityPressSelectsHitAndCanReachBothLimits()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Add(48, 64);
            roll.PressVelocity(1, 7, 2);
            Assert.True(roll.Selection.Contains(0));
            Assert.False(roll.Selection.Contains(48));
            Assert.Equal(7, fixture.Document.Song.Tracks[0].Notes[0].Volume);
            roll.Velocity.Drag(-100);
            Assert.Equal(0, fixture.Document.Song.Tracks[0].Notes[0].Volume);
            roll.Velocity.Drag(100);
            Assert.Equal(15, fixture.Document.Song.Tracks[0].Notes[0].Volume);
            roll.EndDrag();
            Assert.All(fixture.Document.Song.Tracks[0].Notes, note => Assert.Equal(15, note.Volume));
            Assert.Equal(2, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>空白や動かない音量入力は履歴を増やさず、トラック切替で確定する。</summary>
        [Fact]
        public void VelocityNoOpAndTrackSwitchRespectGestureBoundary()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Add(96, 64);
            fixture.Presenter.Undo();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.PressVelocity(48, 7, 2);
            Assert.False(roll.Velocity.IsDragging);
            roll.PressVelocity(0, 15, 2);
            roll.EndDrag();
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.Equal(1, fixture.Document.Session.History.RedoCount);
            roll.PressVelocity(0, 15, 2);
            roll.Velocity.Drag(8);
            roll.SelectTrack(1);
            Assert.False(roll.Velocity.IsDragging);
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            Assert.Equal(0, fixture.Document.Session.History.RedoCount);
            roll.Velocity.Drag(1);
            Assert.Equal(8, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Volume);
        }
    }
}
