using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Session
{
    /// <summary>一括編集の JSON 入力・履歴単位・失敗時の原子性を検証する。</summary>
    public sealed class BatchOperationTests
    {
        private const int TestTempo = 150;
        private const int TestLengthTicks = 768;

        /// <summary>全操作種別を含むバッチを一回だけ戻し、同じ完成状態へやり直せる。</summary>
        [Fact]
        public void AllOperationKindsCommitAsOneUndoEntry()
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSession(directory);
                Song song = Assert.IsType<Song>(session.Song);
                Track firstTrack = song.Tracks[0];
                string original = SongSerializer.Serialize(song);
                string operations = "[" +
                    "{\"kind\":\"AddInstrument\",\"instrument\":{\"id\":2,\"name\":\"lead\",\"kind\":\"NesPulse\",\"duty\":\"Percent25\"}}," +
                    "{\"kind\":\"AddInstrument\",\"instrument\":{\"id\":3,\"name\":\"unused\",\"kind\":\"NesPulse\"}}," +
                    "{\"kind\":\"UpdateInstrument\",\"instrument\":{\"id\":2,\"name\":\"updated\",\"kind\":\"NesPulse\",\"duty\":\"Percent75\"}}," +
                    "{\"kind\":\"RemoveInstrument\",\"instrumentId\":3}," +
                    "{\"kind\":\"AddNote\",\"track\":0,\"tick\":0,\"durationTicks\":24,\"midiNote\":60,\"volume\":15,\"instrumentId\":2,\"effects\":[]}," +
                    "{\"kind\":\"AddNote\",\"track\":0,\"tick\":96,\"durationTicks\":24,\"midiNote\":64}," +
                    "{\"kind\":\"RemoveNote\",\"track\":0,\"tick\":96}," +
                    "{\"kind\":\"MoveNote\",\"track\":0,\"tick\":0,\"toTick\":48,\"midiNote\":67}," +
                    "{\"kind\":\"ResizeNote\",\"track\":0,\"tick\":48,\"durationTicks\":36}," +
                    "{\"kind\":\"UpdateNote\",\"track\":0,\"tick\":48,\"volume\":9,\"effects\":[{\"kind\":\"PitchSlide\",\"value\":-4}]}," +
                    "{\"kind\":\"SetTempo\",\"tempoBpm\":180}," +
                    "{\"kind\":\"SetLength\",\"lengthTicks\":384}," +
                    "{\"kind\":\"SetLoopStart\",\"loopStartTick\":48}," +
                    "{\"kind\":\"SetTrackMuted\",\"track\":1,\"muted\":true}," +
                    "{\"kind\":\"SetTrackPan\",\"track\":0,\"pan\":-0.5}" +
                    "]";

                ApplyJson(session, operations);

                Note note = Assert.Single(firstTrack.Notes);
                Assert.Equal(48, note.Tick);
                Assert.Equal(36, note.DurationTicks);
                Assert.Equal(67, note.MidiNote);
                Assert.Equal(9, note.Volume);
                Assert.Equal(2, note.InstrumentId);
                Assert.Equal(new NoteEffect(NoteEffectKind.PitchSlide, -4), Assert.Single(note.Effects));
                Assert.Equal(180, song.TempoBpm);
                Assert.Equal(384, song.LengthTicks);
                Assert.Equal(48, song.LoopStartTick);
                Assert.True(song.Tracks[1].Muted);
                Assert.Equal(-0.5, firstTrack.Pan);
                Assert.Equal(2, song.Instruments.Count);
                NesPulseInstrument instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[1]);
                Assert.Equal(2, instrument.Id);
                Assert.Equal("updated", instrument.Name);
                Assert.Equal(DutyCycle.Percent75, instrument.Duty);
                Assert.Equal(1, session.History.UndoCount);
                Assert.Equal(0, session.History.RedoCount);
                string completed = SongSerializer.Serialize(song);
                Assert.Equal(completed, File.ReadAllText(session.Path!));

                Assert.True(session.Undo());
                Assert.Equal(original, SongSerializer.Serialize(song));
                Assert.Equal(original, File.ReadAllText(session.Path!));
                Assert.Equal(0, session.History.UndoCount);
                Assert.Equal(1, session.History.RedoCount);
                Assert.True(session.Redo());
                Assert.Equal(completed, SongSerializer.Serialize(song));
                Assert.Equal(completed, File.ReadAllText(session.Path!));
                Assert.Equal(1, session.History.UndoCount);
                Assert.Same(song, session.Song);
                Assert.Same(firstTrack, song.Tracks[0]);
            });
        }

        /// <summary>保存形式の id・name・kind 順と kind 先頭のどちらでも音色を復元する。</summary>
        [Theory]
        [InlineData("{\"id\":2,\"name\":\"lead\",\"kind\":\"NesPulse\",\"duty\":\"Percent25\"}")]
        [InlineData("{\"kind\":\"NesPulse\",\"id\":2,\"name\":\"lead\",\"duty\":\"Percent25\"}")]
        [InlineData("{\"id\":2,\"name\":\"lead\",\"kind\":1,\"duty\":2}")]
        public void InstrumentJsonAcceptsDocumentPropertyOrder(string instrumentJson)
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSession(directory);
                ApplyJson(session, "[{\"kind\":\"AddInstrument\",\"instrument\":" + instrumentJson + "}]");
                Song saved = SongSerializer.Load(session.Path!);
                NesPulseInstrument instrument = Assert.IsType<NesPulseInstrument>(saved.Instruments[1]);
                Assert.Equal(2, instrument.Id);
                Assert.Equal("lead", instrument.Name);
                Assert.Equal(DutyCycle.Percent25, instrument.Duty);
            });
        }

        /// <summary>UpdateNote の省略項目を保持し、明示した空配列だけが効果を解除する。</summary>
        [Fact]
        public void UpdateNotePreservesOmittedFieldsAndCanClearEffects()
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSession(directory);
                session.Notes.Add(0, new Note
                {
                    Tick = 24, DurationTicks = 36, MidiNote = 64, Volume = 7,
                    Effects = new[] { new NoteEffect(NoteEffectKind.Vibrato, 20) }
                });
                Song song = Assert.IsType<Song>(session.Song);
                ApplyJson(session, "[{\"kind\":\"UpdateNote\",\"track\":0,\"tick\":24,\"midiNote\":67}]");
                Note changed = Assert.Single(song.Tracks[0].Notes);
                Assert.Equal(24, changed.Tick);
                Assert.Equal(36, changed.DurationTicks);
                Assert.Equal(67, changed.MidiNote);
                Assert.Equal(7, changed.Volume);
                Assert.Equal(1, changed.InstrumentId);
                Assert.Equal(new NoteEffect(NoteEffectKind.Vibrato, 20), Assert.Single(changed.Effects));

                ApplyJson(session, "[{\"kind\":\"UpdateNote\",\"track\":0,\"tick\":24,\"toTick\":48,\"effects\":[]}]");
                Note cleared = Assert.Single(song.Tracks[0].Notes);
                Assert.Equal(48, cleared.Tick);
                Assert.Equal(36, cleared.DurationTicks);
                Assert.Equal(67, cleared.MidiNote);
                Assert.Equal(7, cleared.Volume);
                Assert.Empty(cleared.Effects);
                Assert.True(session.Undo());
                Assert.Single(Assert.Single(song.Tracks[0].Notes).Effects);
            });
        }

        /// <summary>トラック省略時に既定値を使い、明示値を優先する。</summary>
        [Fact]
        public void ExplicitTrackOverridesBatchDefault()
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSession(directory);
                string operations = "[" +
                    "{\"kind\":\"AddNote\",\"tick\":0,\"durationTicks\":24,\"midiNote\":60}," +
                    "{\"kind\":\"AddNote\",\"track\":0,\"tick\":0,\"durationTicks\":24,\"midiNote\":67}]";
                BatchOperationApplier.Apply(session, BatchOperationJson.Deserialize(operations), 1);
                Song song = Assert.IsType<Song>(session.Song);
                Assert.Equal(67, Assert.Single(song.Tracks[0].Notes).MidiNote);
                Assert.Equal(60, Assert.Single(song.Tracks[1].Notes).MidiNote);
                Assert.Equal(1, session.History.UndoCount);
            });
        }

        /// <summary>後続ノートの重複エラーが、それ以前の変更と redo 破棄を一切公開しない。</summary>
        [Fact]
        public void FailedBatchPreservesMemoryFileAndBothHistoryStacks()
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSessionWithUndoAndRedo(directory);
                Song song = Assert.IsType<Song>(session.Song);
                Note originalNote = Assert.Single(song.Tracks[0].Notes);
                string original = SongSerializer.Serialize(song);
                string operations = "[" +
                    "{\"kind\":\"SetTempo\",\"tempoBpm\":180}," +
                    "{\"kind\":\"AddNote\",\"track\":0,\"tick\":24,\"durationTicks\":48,\"midiNote\":67}]";
                Assert.Throws<SongValidationException>(() => ApplyJson(session, operations));
                Assert.Equal(original, SongSerializer.Serialize(song));
                Assert.Equal(original, File.ReadAllText(session.Path!));
                Assert.Same(originalNote, Assert.Single(song.Tracks[0].Notes));
                Assert.Equal(1, session.History.UndoCount);
                Assert.Equal(1, session.History.RedoCount);
                Assert.True(session.Redo());
                Assert.Equal(2, song.Tracks[0].Notes.Count);
                Assert.Equal(TestTempo, song.TempoBpm);
            });
        }

        /// <summary>保存先の消失による失敗では、候補ソングと履歴変更を公開しない。</summary>
        [Fact]
        public void SaveFailurePreservesMemoryAndHistory()
        {
            WithTemporaryDirectory(directory =>
            {
                string workingDirectory = Path.Combine(directory, "working");
                Directory.CreateDirectory(workingDirectory);
                EditSession session = CreateSessionWithUndoAndRedo(workingDirectory);
                Song song = Assert.IsType<Song>(session.Song);
                string original = SongSerializer.Serialize(song);
                Directory.Delete(workingDirectory, true);

                Assert.Throws<DirectoryNotFoundException>(() =>
                    ApplyJson(session, "[{\"kind\":\"SetTempo\",\"tempoBpm\":180}]"));

                Assert.Equal(original, SongSerializer.Serialize(song));
                Assert.Equal(1, session.History.UndoCount);
                Assert.Equal(1, session.History.RedoCount);
                Assert.False(File.Exists(session.Path!));
                Assert.False(Directory.Exists(workingDirectory));
            });
        }

        /// <summary>空のバッチは保存せず操作エラーとして拒否する。</summary>
        [Fact]
        public void EmptyBatchIsRejectedWithoutChangingSong()
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSession(directory);
                string original = File.ReadAllText(session.Path!);
                Assert.Throws<ArgumentException>(() => ApplyJson(session, "[]"));
                Assert.Equal(original, File.ReadAllText(session.Path!));
                Assert.Equal(0, session.History.UndoCount);
            });
        }

        /// <summary>不正 JSON・null 要素・未知種別・未知プロパティを操作エラーへ統一する。</summary>
        [Theory]
        [InlineData("{")]
        [InlineData("null")]
        [InlineData("{}")]
        [InlineData("[null]")]
        [InlineData("[false]")]
        [InlineData("[[]]")]
        [InlineData("[{}]")]
        [InlineData("[{\"kind\":\"Unknown\"}]")]
        [InlineData("[{\"kind\":\"None\"}]")]
        [InlineData("[{\"kind\":999}]")]
        [InlineData("[{\"kind\":\"SetTempo\",\"tempoBpm\":180,\"unknown\":1}]")]
        [InlineData("[{\"kind\":\"AddInstrument\",\"instrument\":{\"id\":2,\"kind\":\"NesPulse\",\"unknown\":1}}]")]
        public void MalformedOperationsAreArgumentErrors(string operations)
        {
            Assert.Throws<ArgumentException>(() => BatchOperationJson.Deserialize(operations));
        }

        /// <summary>各操作の必須項目が欠ける場合は、既存の曲と履歴を維持する。</summary>
        [Theory]
        [InlineData("{\"kind\":\"AddNote\",\"tick\":0,\"durationTicks\":24,\"midiNote\":60}")]
        [InlineData("{\"kind\":\"AddNote\",\"track\":0,\"durationTicks\":24,\"midiNote\":60}")]
        [InlineData("{\"kind\":\"AddNote\",\"track\":0,\"tick\":0,\"midiNote\":60}")]
        [InlineData("{\"kind\":\"AddNote\",\"track\":0,\"tick\":0,\"durationTicks\":24}")]
        [InlineData("{\"kind\":\"RemoveNote\",\"track\":0}")]
        [InlineData("{\"kind\":\"MoveNote\",\"track\":0,\"tick\":0}")]
        [InlineData("{\"kind\":\"ResizeNote\",\"track\":0,\"tick\":0}")]
        [InlineData("{\"kind\":\"UpdateNote\",\"track\":0}")]
        [InlineData("{\"kind\":\"AddInstrument\"}")]
        [InlineData("{\"kind\":\"UpdateInstrument\"}")]
        [InlineData("{\"kind\":\"RemoveInstrument\"}")]
        [InlineData("{\"kind\":\"SetTempo\"}")]
        [InlineData("{\"kind\":\"SetLength\"}")]
        [InlineData("{\"kind\":\"SetLoopStart\"}")]
        [InlineData("{\"kind\":\"SetTrackMuted\",\"track\":0}")]
        [InlineData("{\"kind\":\"SetTrackPan\",\"track\":0}")]
        public void MissingRequiredFieldsLeaveStateUnchanged(string operation)
        {
            WithTemporaryDirectory(directory =>
            {
                EditSession session = CreateSessionWithUndoAndRedo(directory);
                Song song = Assert.IsType<Song>(session.Song);
                string original = SongSerializer.Serialize(song);
                Assert.Throws<ArgumentException>(() => ApplyJson(session, "[" + operation + "]"));
                Assert.Equal(original, SongSerializer.Serialize(song));
                Assert.Equal(original, File.ReadAllText(session.Path!));
                Assert.Equal(1, session.History.UndoCount);
                Assert.Equal(1, session.History.RedoCount);
            });
        }

        private static void ApplyJson(EditSession session, string operations)
        {
            BatchOperationApplier.Apply(session, BatchOperationJson.Deserialize(operations));
        }

        private static EditSession CreateSession(string directory)
        {
            EditSession session = new EditSession();
            session.New(Path.Combine(directory, "song.arpeggio.json"), ChipKind.Nes, TestTempo, TestLengthTicks);
            return session;
        }

        private static EditSession CreateSessionWithUndoAndRedo(string directory)
        {
            EditSession session = CreateSession(directory);
            session.Notes.Add(0, new Note());
            session.Notes.Add(0, new Note { Tick = 96 });
            Assert.True(session.Undo());
            return session;
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-batch-tests-" + Guid.NewGuid().ToString("N"));
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
