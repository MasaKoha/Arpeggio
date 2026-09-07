using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Session
{
    /// <summary>保存・履歴・再生側参照を含めた編集操作を検証する。</summary>
    public sealed class EditSessionTests
    {
        /// <summary>新規作成・自動保存・再オープンが同じソングを保持する。</summary>
        [Fact]
        public void NewAndEditingPersistAndOpenResetsHistory()
        {
            WithTemporaryDirectory(directory =>
            {
                string path = System.IO.Path.Combine(directory, "song.arpeggio.json");
                var session = new EditSession();
                Assert.Null(session.Song);
                Assert.Null(session.Path);
                session.New(path, ChipKind.Nes, 150, 768);
                session.Notes.Add(0, new Note());
                Assert.Single(SongSerializer.Load(path).Tracks[0].Notes);
                Assert.Equal(1, session.History.UndoCount);
                session.Open(path);
                Assert.Equal(0, session.History.UndoCount);
                Assert.Equal(path, session.Path);
                Assert.False(session.Undo());
                Assert.False(session.Redo());
            });
        }

        /// <summary>追加・移動・長さ変更・音量変更・削除と undo/redo が参照を維持する。</summary>
        [Fact]
        public void NoteEditingPreservesSongAndTrackReferences()
        {
            WithTemporaryDirectory(directory =>
            {
                var session = CreateSession(directory);
                Song song = Assert.IsType<Song>(session.Song);
                Track track = song.Tracks[0];
                session.Notes.Add(0, new Note { Tick = 96 });
                session.Notes.Add(0, new Note { Tick = 0 });
                var beforeMove = track.Notes;
                session.Notes.Move(0, 96, 48, 67);
                Assert.Equal(96, beforeMove[1].Tick);
                Assert.NotSame(beforeMove, track.Notes);
                session.Notes.Resize(0, 48, 24);
                session.Notes.SetVolume(0, 48, 7);
                Assert.Equal(67, track.Notes[1].MidiNote);
                Assert.Equal(24, track.Notes[1].DurationTicks);
                Assert.Equal(7, track.Notes[1].Volume);
                session.Notes.Remove(0, 0);
                Assert.Single(track.Notes);
                Assert.True(session.Undo());
                Assert.Equal(2, track.Notes.Count);
                Assert.True(session.Redo());
                Assert.Single(track.Notes);
                Assert.Same(song, session.Song);
                Assert.Same(track, song.Tracks[0]);
                Assert.Equal(SongSerializer.Serialize(song), File.ReadAllText(session.Path!));
            });
        }

        /// <summary>未変更トラック・音色・同一トラック内の未変更ノートを編集と履歴移動で維持する。</summary>
        [Fact]
        public void UnchangedReferencesSurviveEditsAndUndoRedo()
        {
            WithTemporaryDirectory(directory =>
            {
                var session = CreateSession(directory);
                session.Notes.Add(0, new Note());
                session.Notes.Add(0, new Note { Tick = 96 });
                session.Notes.Add(1, new Note());
                Song song = Assert.IsType<Song>(session.Song);
                var instruments = song.Instruments;
                Instrument instrument = instruments[0];
                var otherNotes = song.Tracks[1].Notes;
                Note firstNote = song.Tracks[0].Notes[0];
                session.Notes.SetVolume(0, 96, 7);
                Assert.Same(instruments, song.Instruments);
                Assert.Same(instrument, song.Instruments[0]);
                Assert.Same(otherNotes, song.Tracks[1].Notes);
                Assert.Same(firstNote, song.Tracks[0].Notes[0]);
                Assert.True(session.Undo());
                Assert.Same(otherNotes, song.Tracks[1].Notes);
                Assert.Same(firstNote, song.Tracks[0].Notes[0]);
                Assert.True(session.Redo());
                Assert.Same(otherNotes, song.Tracks[1].Notes);
                Assert.Same(firstNote, song.Tracks[0].Notes[0]);
                session.Instruments.Add(new NesPulseInstrument { Id = 2 });
                Assert.Same(instrument, song.Instruments[0]);
                Assert.Same(firstNote, song.Tracks[0].Notes[0]);
                session.Instruments.Update(new NesPulseInstrument { Id = 2, Duty = DutyCycle.Percent25 });
                Assert.Same(instrument, song.Instruments[0]);
                Assert.Same(otherNotes, song.Tracks[1].Notes);
            });
        }

        /// <summary>音色の追加・更新・削除は参照制約を検証し、外部オブジェクトから独立する。</summary>
        [Fact]
        public void InstrumentEditingValidatesReferencesAndCopiesInput()
        {
            WithTemporaryDirectory(directory =>
            {
                var session = CreateSession(directory);
                Song song = Assert.IsType<Song>(session.Song);
                var instrument = new NesPulseInstrument { Id = 2, Name = "second", VolumeMacro = new Macro { Values = new[] { 15, 7 } } };
                session.Instruments.Add(instrument);
                instrument.Name = "external mutation";
                instrument.VolumeMacro.Values[0] = 0;
                Assert.Equal("second", song.Instruments[1].Name);
                Assert.Equal(15, Assert.IsType<NesPulseInstrument>(song.Instruments[1]).VolumeMacro!.Values[0]);
                session.Instruments.Update(new NesPulseInstrument { Id = 2, Name = "updated", Duty = DutyCycle.Percent25 });
                Assert.Equal("updated", song.Instruments[1].Name);
                session.Notes.Add(0, new Note { InstrumentId = 2 });
                string before = SongSerializer.Serialize(song);
                int undoCount = session.History.UndoCount;
                Assert.Throws<InvalidOperationException>(() => session.Instruments.Remove(2));
                Assert.Equal(before, SongSerializer.Serialize(song));
                Assert.Equal(undoCount, session.History.UndoCount);
                session.Notes.Remove(0, 0);
                session.Instruments.Remove(2);
                Assert.Single(song.Instruments);
            });
        }

        /// <summary>重複ノート・不正な音色更新の失敗でソングと履歴とファイルを変更しない。</summary>
        [Fact]
        public void FailedEditsLeaveMemoryHistoryAndFileUntouched()
        {
            WithTemporaryDirectory(directory =>
            {
                var session = CreateSession(directory);
                session.Notes.Add(0, new Note());
                Song song = Assert.IsType<Song>(session.Song);
                string before = SongSerializer.Serialize(song);
                int undoCount = session.History.UndoCount;
                Assert.Throws<SongValidationException>(() => session.Notes.Add(0, new Note { Tick = 24 }));
                Assert.Throws<SongValidationException>(() => session.Notes.Resize(0, 0, 0));
                Assert.Throws<SongValidationException>(() => session.Instruments.Update(new NesTriangleInstrument { Id = 1 }));
                Assert.Equal(before, SongSerializer.Serialize(song));
                Assert.Equal(before, File.ReadAllText(session.Path!));
                Assert.Equal(undoCount, session.History.UndoCount);
            });
        }

        /// <summary>保存先が失われた場合、編集と undo はメモリと履歴を保持する。</summary>
        [Fact]
        public void SaveFailureDoesNotCommitEditOrUndo()
        {
            WithTemporaryDirectory(directory =>
            {
                string workingDirectory = System.IO.Path.Combine(directory, "working");
                Directory.CreateDirectory(workingDirectory);
                var session = CreateSession(workingDirectory);
                session.Notes.Add(0, new Note());
                Song song = Assert.IsType<Song>(session.Song);
                string before = SongSerializer.Serialize(song);
                int undoCount = session.History.UndoCount;
                Directory.Delete(workingDirectory, true);
                Assert.Throws<DirectoryNotFoundException>(() => session.Notes.SetVolume(0, 0, 7));
                Assert.Throws<DirectoryNotFoundException>(() => session.Undo());
                Assert.Equal(before, SongSerializer.Serialize(song));
                Assert.Equal(undoCount, session.History.UndoCount);
                Assert.Equal(0, session.History.RedoCount);
            });
        }

        /// <summary>新規上書きと破損ファイルの Open は現在のセッションを壊さない。</summary>
        [Fact]
        public void FailedNewAndOpenKeepCurrentSession()
        {
            WithTemporaryDirectory(directory =>
            {
                var session = CreateSession(directory);
                Song song = Assert.IsType<Song>(session.Song);
                Assert.Throws<ArgumentException>(() => session.New(session.Path!, ChipKind.Snes, 150, 768));
                string invalidPath = System.IO.Path.Combine(directory, "invalid.json");
                File.WriteAllText(invalidPath, "{}");
                Assert.Throws<SongValidationException>(() => session.Open(invalidPath));
                Assert.Same(song, session.Song);
            });
        }

        private static EditSession CreateSession(string directory)
        {
            var session = new EditSession();
            session.New(System.IO.Path.Combine(directory, "song.arpeggio.json"), ChipKind.Nes, 150, 768);
            return session;
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arpeggio-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
