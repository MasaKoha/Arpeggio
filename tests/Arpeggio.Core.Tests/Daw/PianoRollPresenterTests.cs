using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>座標から独立したノート操作とドラッグ履歴を検証する。</summary>
    public sealed class PianoRollPresenterTests
    {
        /// <summary>多数の移動通知でも一回の undo で押下時点へ戻る。</summary>
        [Fact]
        public void DragPublishesEachMovementButRecordsOneHistoryEntry()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Add(0, 60);
            int historyBeforeDrag = fixture.Document.Session.History.UndoCount;
            List<Note> originalNotes = fixture.Document.Song.Tracks[0].Notes;
            pianoRoll.Press(1, 60, resizeToleranceTicks: 2, bypassSnap: false);
            Assert.Equal(PianoRollDragMode.Move, pianoRoll.DragMode);
            pianoRoll.Drag(13, 61, bypassSnap: false);
            Assert.NotSame(originalNotes, fixture.Document.Song.Tracks[0].Notes);
            Assert.Equal(12, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            pianoRoll.Drag(25, 62, bypassSnap: false);
            pianoRoll.Drag(49, 64, bypassSnap: false);
            pianoRoll.EndDrag();

            Assert.Equal(historyBeforeDrag + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Note restored = Assert.Single(fixture.Document.Song.Tracks[0].Notes);
            Assert.Equal(0, restored.Tick);
            Assert.Equal(60, restored.MidiNote);
            fixture.Presenter.Redo();
            Assert.Equal(48, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
        }

        /// <summary>押下だけの操作は既存の redo 履歴を消さない。</summary>
        [Fact]
        public void StationaryGestureKeepsRedoHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Add(0, 60);
            pianoRoll.Add(96, 64);
            fixture.Presenter.Undo();
            pianoRoll.Press(1, 60, resizeToleranceTicks: 2, bypassSnap: false);
            pianoRoll.EndDrag();

            Assert.Equal(1, fixture.Document.Session.History.RedoCount);
            fixture.Presenter.Redo();
            Assert.Equal(2, fixture.Document.Song.Tracks[0].Notes.Count);
        }

        /// <summary>右端のドラッグで長さを変更し、次の空白クリックでもその長さを使う。</summary>
        [Fact]
        public void ResizeSetsDurationOfNextAddedNote()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Press(0, 60, resizeToleranceTicks: 2, bypassSnap: false);
            Assert.Equal(24, Assert.Single(fixture.Document.Song.Tracks[0].Notes).DurationTicks);
            pianoRoll.Press(23, 60, resizeToleranceTicks: 2, bypassSnap: false);
            Assert.Equal(PianoRollDragMode.Resize, pianoRoll.DragMode);
            pianoRoll.Drag(47, 60, bypassSnap: false);
            pianoRoll.EndDrag();
            Assert.Equal(48, Assert.Single(fixture.Document.Song.Tracks[0].Notes).DurationTicks);
            pianoRoll.Press(96, 64, resizeToleranceTicks: 2, bypassSnap: false);
            Assert.Equal(48, fixture.Document.Song.Tracks[0].Notes[1].DurationTicks);
        }

        /// <summary>Alt による移動はグリッド外の整数 tick へ置ける。</summary>
        [Fact]
        public void AltDragBypassesGrid()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Add(0, 60);
            pianoRoll.Press(1, 60, resizeToleranceTicks: 2, bypassSnap: false);
            pianoRoll.Drag(8, 60, bypassSnap: true);
            pianoRoll.EndDrag();

            Assert.Equal(7, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
        }

        /// <summary>ヒット判定と右クリック削除は選択トラックに限定する。</summary>
        [Fact]
        public void HitTestAndRightClickIgnoreGhostTracks()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Add(0, 60);
            pianoRoll.SelectTrack(1);
            Assert.Null(pianoRoll.HitTest(1, 60));
            pianoRoll.DeleteAt(1, 60);
            Assert.Single(fixture.Document.Song.Tracks[0].Notes);
            pianoRoll.Add(0, 64);
            pianoRoll.DeleteAt(1, 64);
            Assert.Empty(fixture.Document.Song.Tracks[1].Notes);
            Assert.Single(fixture.Document.Song.Tracks[0].Notes);
        }

        /// <summary>音量変更は上下限で飽和する。</summary>
        [Fact]
        public void VolumeIsClampedToChipRange()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter pianoRoll = fixture.Presenter.PianoRoll;
            pianoRoll.Add(0, 60);
            pianoRoll.ChangeVolume(1);
            Assert.Equal(15, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Volume);
            pianoRoll.ChangeVolume(-1);
            Assert.Equal(14, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Volume);
            pianoRoll.ChangeVolume(-100);
            Assert.Equal(0, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Volume);
        }
    }
}
