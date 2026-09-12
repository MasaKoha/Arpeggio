using System.Linq;
using Arpeggio.Core.Document;
using Xunit;
using Arpeggio.Daw.Presenters.PianoRoll;
using Arpeggio.Daw.Presenters.PianoRoll.Selection;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>矩形・複数選択と原子的なノート変形を検証する。</summary>
    public sealed class PianoRollSelectionTests
    {
        /// <summary>部分重なり・端への接触・逆向きドラッグを含めて矩形内だけ選ぶ。</summary>
        [Theory]
        [InlineData(12, 59, 48, 64, new[] { 0, 48 })]
        [InlineData(48, 59, 12, 64, new[] { 0, 48 })]
        [InlineData(24, 59, 47, 60, new[] { 0 })]
        [InlineData(24.01, 59, 47, 60, new int[] { })]
        [InlineData(0, 59, 120, 64, new[] { 0, 48 })]
        public void RectangleSelectsIntersectingNotes(double startTick, int startPitch, double endTick, int endPitch, int[] expected)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 24, 60);
            AddNote(fixture, 48, 24, 64);
            AddNote(fixture, 96, 24, 72);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(startTick, startPitch, 2, false, NotePointerModifiers.Control);
            roll.Drag(endTick, endPitch, false);
            Assert.NotNull(roll.SelectionRectangle);
            Assert.Equal(expected, roll.Selection.Resolve(fixture.Document.Song.Tracks[0].Notes).Select(note => note.Tick));
            roll.EndDrag();
            Assert.Null(roll.SelectionRectangle);
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>Shift の追加・解除は履歴を作らず redo も維持する。</summary>
        [Fact]
        public void ToggleAndRectanglePreserveRedoHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Add(48, 64);
            roll.Add(96, 67);
            fixture.Presenter.Undo();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(1, 60, 2, false, NotePointerModifiers.Shift);
            roll.Press(49, 64, 2, false, NotePointerModifiers.Shift);
            Assert.Equal(2, roll.Selection.Count);
            roll.Press(1, 60, 2, false, NotePointerModifiers.Shift);
            Assert.False(roll.Selection.Contains(0));
            Assert.True(roll.Selection.Contains(48));
            roll.Press(0, 59, 2, false, NotePointerModifiers.Control);
            roll.Drag(72, 64, false);
            roll.EndDrag();
            Assert.Equal(2, roll.Selection.Count);
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.Equal(1, fixture.Document.Session.History.RedoCount);
        }

        /// <summary>二音以上の選択中は空白を押して動かしても追加しない。</summary>
        [Fact]
        public void BlankPressOnlyClearsMultipleSelection()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Add(48, 64);
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(192, 67, 2, false);
            roll.Drag(240, 67, false);
            roll.EndDrag();
            Assert.Equal(2, fixture.Document.Song.Tracks[0].Notes.Count);
            Assert.Equal(0, roll.Selection.Count);
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.DoesNotContain("音選択", fixture.View.Status);
        }

        /// <summary>隣接する選択同士の移動を中間重複で拒否せず、相対位置と属性を保つ。</summary>
        [Fact]
        public void AdjacentNotesMoveTogetherInOneGesture()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 24, 60);
            AddNote(fixture, 24, 24, 64);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.SelectAll();
            Note[] original = fixture.Document.Song.Tracks[0].Notes.ToArray();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(1, 60, 2, false);
            roll.Drag(13, 61, false);
            roll.Drag(49, 65, false);
            roll.EndDrag();
            Assert.Equal(new[] { 48, 72 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
            Assert.Equal(new[] { 65, 69 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.MidiNote));
            Assert.Equal(new[] { 0, 24 }, original.Select(note => note.Tick));
            Assert.True(roll.Selection.Contains(48));
            Assert.True(roll.Selection.Contains(72));
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 0, 24 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
        }

        /// <summary>一音だけが範囲外になる場合もソング・選択・履歴を全件維持する。</summary>
        [Theory]
        [InlineData(-12, 0)]
        [InlineData(720, 0)]
        [InlineData(0, 64)]
        [InlineData(0, -61)]
        public void InvalidGroupMoveIsAtomic(int tickDelta, int pitchDelta)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 24, 60);
            AddNote(fixture, 48, 24, 64);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.SelectAll();
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            Assert.Throws<SongValidationException>(() => roll.MoveSelection(tickDelta, pitchDelta));
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.True(roll.Selection.Contains(0));
            Assert.True(roll.Selection.Contains(48));
        }

        /// <summary>ドラッグ先の一音が重複したら直前の有効位置を全件保つ。</summary>
        [Fact]
        public void InvalidDragKeepsLastValidGroupAndOneHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 24, 60);
            AddNote(fixture, 48, 24, 64);
            AddNote(fixture, 120, 24, 67);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Press(1, 60, 2, false, NotePointerModifiers.Shift);
            roll.Press(49, 64, 2, false, NotePointerModifiers.Shift);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(1, 60, 2, false);
            roll.Drag(13, 61, false);
            string valid = SongSerializer.Serialize(fixture.Document.Song);
            Assert.Throws<SongValidationException>(() => roll.Drag(61, 61, false));
            Assert.Equal(valid, SongSerializer.Serialize(fixture.Document.Song));
            Assert.True(roll.Selection.Contains(12));
            Assert.True(roll.Selection.Contains(60));
            roll.EndDrag();
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 0, 48, 120 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
        }

        /// <summary>長さは同じ差分で変化し、各音を最短一 tick で止める。</summary>
        [Theory]
        [InlineData(17, 18, 30)]
        [InlineData(5, 6, 18)]
        [InlineData(-9, 1, 4)]
        public void ResizeUsesSharedDeltaAndOneTickMinimum(double destination, int firstDuration, int secondDuration)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 12, 60);
            AddNote(fixture, 96, 24, 64);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(11, 60, 2, false);
            roll.Drag(destination, 60, true);
            roll.EndDrag();
            Assert.Equal(new[] { firstDuration, secondDuration }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.DurationTicks));
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>追加中の横移動を一履歴にまとめ、確定した長さを次の追加に使う。</summary>
        [Fact]
        public void CreationDragSharesHistoryWithAddition()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Press(0, 60, 2, false);
            Assert.Equal(24, Assert.Single(fixture.Document.Song.Tracks[0].Notes).DurationTicks);
            roll.Drag(36, 60, false);
            roll.Drag(48, 60, false);
            roll.EndDrag();
            Assert.Equal(1, fixture.Document.Session.History.UndoCount);
            roll.Add(96, 64);
            Assert.Equal(new[] { 48, 48 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.DurationTicks));
            fixture.Presenter.Undo();
            fixture.Presenter.Undo();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);
        }

        /// <summary>掴んだ既存音の長さも次の追加へ引き継ぐ。</summary>
        [Fact]
        public void GrabbingAnExistingNoteRemembersItsDuration()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 36, 60);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Press(1, 60, 2, false);
            roll.EndDrag();
            roll.Add(96, 64);
            Assert.Equal(36, fixture.Document.Song.Tracks[0].Notes[1].DurationTicks);
        }

        /// <summary>先頭以外の選択音を掴んでも相対位置と選択件数を保持する。</summary>
        [Fact]
        public void DraggingSecondSelectedNoteKeepsWholeSelection()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            AddNote(fixture, 0, 12, 60);
            AddNote(fixture, 48, 36, 64);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.SelectAll();
            roll.Press(49, 64, 2, false);
            roll.Drag(61, 65, false);
            roll.EndDrag();
            Assert.Equal(new[] { 12, 60 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
            Assert.Equal(2, roll.Selection.Count);
            roll.ClearSelection();
            roll.Add(192, 67);
            Assert.Equal(36, fixture.Document.Song.Tracks[0].Notes[2].DurationTicks);
        }

        /// <summary>長さ変更が一音でも重複または効果制約違反になる場合は全件拒否する。</summary>
        [Theory]
        [InlineData(95)]
        [InlineData(0)]
        public void InvalidResizeKeepsAllNotesAndEffects(double pointerTick)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Document.Session.Notes.Add(0, new Note
            {
                Tick = 0, DurationTicks = 24, MidiNote = 60,
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 12) }
            });
            AddNote(fixture, 48, 24, 64);
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.SelectAll();
            string original = SongSerializer.Serialize(fixture.Document.Song);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Press(23, 60, 2, false);
            Assert.Throws<SongValidationException>(() => roll.Drag(pointerTick, 60, true));
            roll.EndDrag();
            Assert.Equal(original, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
        }

        private static void AddNote(DawPresenterFixture fixture, int tick, int duration, int pitch) =>
            fixture.Document.Session.Notes.Add(0, new Note { Tick = tick, DurationTicks = duration, MidiNote = pitch });
    }
}
