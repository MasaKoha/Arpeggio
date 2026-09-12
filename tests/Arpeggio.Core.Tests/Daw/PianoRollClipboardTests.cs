using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Xunit;
using Arpeggio.Daw.Presenters.PianoRoll;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>クリップボードの属性保存・上書き・トラック境界を検証する。</summary>
    public sealed class PianoRollClipboardTests
    {
        /// <summary>コピーは元を残し、相対位置と全属性を保持して別位置へ貼り付ける。</summary>
        [Fact]
        public void CopyPastePreservesOriginalsAndIndependentEffects()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(48, 60);
            fixture.Presenter.Notes.AddEffect(NoteEffectKind.Vibrato, 4);
            roll.ChangeVolume(-3);
            roll.Add(96, 67);
            roll.SelectAll();
            roll.Copy();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Paste(240);
            Note[] notes = fixture.Document.Song.Tracks[0].Notes.ToArray();
            Assert.Equal(new[] { 48, 96, 240, 288 }, notes.Select(note => note.Tick));
            Assert.Equal(notes[0].MidiNote, notes[2].MidiNote);
            Assert.Equal(notes[0].DurationTicks, notes[2].DurationTicks);
            Assert.Equal(notes[0].Volume, notes[2].Volume);
            Assert.Equal(notes[0].InstrumentId, notes[2].InstrumentId);
            Assert.Equal(notes[0].Effects, notes[2].Effects);
            Assert.NotSame(notes[0].Effects, notes[2].Effects);
            Assert.Equal(new[] { 240, 288 }, roll.Selection.Resolve(notes).Select(note => note.Tick));
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            Assert.Empty(SongSerializer.Load(fixture.Path).Tracks[0].Notes);
            fixture.Presenter.Notes.UpdateEffect(0, NoteEffectKind.Vibrato, 8);
            roll.Paste(384);
            Assert.Equal(4, fixture.Document.Song.Tracks[0].Notes.Single(note => note.Tick == 384).Effects[0].Value);
        }

        /// <summary>切り取りは全選択を一履歴で削除し、貼り付け後も undo で両方を戻せる。</summary>
        [Fact]
        public void CutRemovesOriginalsAndKeepsClipboard()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(48, 60);
            roll.Add(96, 64);
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Cut();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            roll.Paste(192);
            Assert.Equal(new[] { 192, 240 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
            fixture.Presenter.Undo();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 48, 96 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
        }

        /// <summary>複製のオフセットは間の空白を含む選択範囲の長さになる。</summary>
        [Fact]
        public void DuplicateUsesSelectionSpanAndSelectsOnlyCopies()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(48, 60);
            roll.Add(96, 64);
            roll.SelectAll();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            roll.Duplicate();
            Assert.Equal(new[] { 48, 96, 120, 168 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
            Assert.Equal(new[] { 120, 168 }, roll.Selection.Resolve(fixture.Document.Song.Tracks[0].Notes).Select(note => note.Tick));
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>部分重なりは音高によらず置き換え、隣接音は残し、件数を表示する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PasteAndDuplicateOverwriteOnlyOverlaps(bool duplicate)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            fixture.Document.Session.Notes.Add(0, new Note { Tick = 0, DurationTicks = 48, MidiNote = 60 });
            fixture.Document.Session.Notes.Add(0, new Note { Tick = 48, DurationTicks = 12, MidiNote = 80 });
            fixture.Document.Session.Notes.Add(0, new Note { Tick = 72, DurationTicks = 36, MidiNote = 90 });
            fixture.Document.Session.Notes.Add(0, new Note { Tick = 108, DurationTicks = 12, MidiNote = 70 });
            roll.Press(1, 60, 2, false);
            roll.EndDrag();
            int historyBefore = fixture.Document.Session.History.UndoCount;
            if (duplicate) { roll.Duplicate(); }
            else { roll.Copy(); roll.Paste(48); }
            Assert.Equal(new[] { 0, 48, 108 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
            Assert.Equal(60, fixture.Document.Song.Tracks[0].Notes[1].MidiNote);
            Assert.Contains("2 音を置き換え", fixture.View.Status);
            Assert.Contains("1 音選択", fixture.View.Status);
            Assert.Equal(historyBefore + 1, fixture.Document.Session.History.UndoCount);
            fixture.Presenter.Undo();
            Assert.Equal(new[] { 0, 48, 72, 108 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
        }

        /// <summary>範囲外の貼り付けは置換予定の音も削除せず、作業ファイルも維持する。</summary>
        [Fact]
        public void InvalidPasteDoesNotDeleteOverlapOrChangeHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Copy();
            fixture.Document.Session.Notes.Add(0, new Note { Tick = 756, DurationTicks = 12, MidiNote = 64 });
            string before = SongSerializer.Serialize(fixture.Document.Song);
            string savedBefore = File.ReadAllText(fixture.Document.Session.Path!);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            Assert.Throws<SongValidationException>(() => roll.Paste(756));
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(savedBefore, File.ReadAllText(fixture.Document.Session.Path!));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.True(roll.Selection.Contains(0));
        }

        /// <summary>異なるトラックと再オープンした文書への貼り付けは拒否する。</summary>
        [Fact]
        public void ClipboardRejectsDifferentTrackAndDocument()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(0, 60);
            roll.Copy();
            roll.SelectTrack(1);
            Assert.Throws<InvalidOperationException>(() => roll.Paste(96));
            Assert.Empty(fixture.Document.Song.Tracks[1].Notes);
            roll.SelectTrack(0);
            roll.Paste(96);
            Assert.Equal(2, fixture.Document.Song.Tracks[0].Notes.Count);
            fixture.Presenter.Open(fixture.Path);
            Assert.Throws<InvalidOperationException>(() => roll.Paste(192));
        }

        /// <summary>貼り付けはグリッドで丸めず、実際の再生カーソルの整数 tick を使う。</summary>
        [Fact]
        public void MainPresenterPastesAtPlaybackCursor()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Add(240, 60);
            roll.Copy();
            fixture.Presenter.Transport.TogglePlayback();
            fixture.Audio.RequestFrames(4410);
            fixture.Presenter.Poll();
            int cursor = (int)Math.Floor(fixture.View.PositionTick);
            Assert.True(cursor > 0);
            fixture.Presenter.PasteNotesAtCursor();
            Assert.True(roll.Selection.Contains(cursor));
            Assert.Contains(fixture.Document.Song.Tracks[0].Notes, note => note.Tick == 240);
        }

        /// <summary>空クリップボードは履歴を変えず、停止中の未指定位置は先頭になる。</summary>
        [Fact]
        public void EmptyClipboardIsNoOpAndDefaultPasteStartsAtZero()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            PianoRollPresenter roll = fixture.Presenter.PianoRoll;
            roll.Paste();
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
            roll.Add(96, 60);
            roll.Copy();
            fixture.Presenter.PasteNotesAtCursor();
            Assert.True(roll.Selection.Contains(0));
            Assert.Equal(new[] { 0, 96 }, fixture.Document.Song.Tracks[0].Notes.Select(note => note.Tick));
        }
    }
}
