using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Tests.Cli;
using Xunit;

namespace Arpeggio.Core.Tests.Session
{
    /// <summary>乱数候補のセッション公開・保存直前照合と履歴の一体性を検証する。</summary>
    public sealed class SfxRandomizationEditorTests
    {
        /// <summary>読取後の外部更新を保存直前に検出し、公開参照・出自・履歴を維持する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExternalChangeBeforeSaveRejectsRandomization(bool mutate)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            var session = CreateSession(path);
            Song original = session.Song!;
            string originalJson = SongSerializer.Serialize(original);
            Song external = SongSerializer.Load(path);
            external.Title = "外部の変更";
            SongSerializer.Save(external, path);
            byte[] externalBytes = File.ReadAllBytes(path);
            SfxEditException exception = Assert.Throws<SfxEditException>(() => Apply(session, mutate));
            Assert.Equal("RevisionConflict", exception.Code);
            Assert.Same(original, session.Song);
            Assert.Equal(originalJson, SongSerializer.Serialize(session.Song!));
            Assert.Equal(externalBytes, File.ReadAllBytes(path));
            Assert.Equal(0, session.History.UndoCount);
            Assert.Equal(0, session.History.RedoCount);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>予行は候補だけを変更し、一回の適用・Undo・Redo で出自と生成列が同時に戻る。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CandidateAndHistoryAreIndependent(bool mutate)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            var session = CreateSession(path);
            Song original = session.Song!;
            string originalJson = SongSerializer.Serialize(original);
            SfxEditResult preview = Apply(session, mutate, true);
            Assert.Same(original, session.Song);
            Assert.Equal(originalJson, File.ReadAllText(path));
            Assert.NotEqual(preview.Revision, preview.CandidateRevision);
            Assert.Equal(0, session.History.UndoCount);
            SfxEditResult applied = Apply(session, mutate);
            Assert.Equal(preview.CandidateRevision, applied.Revision);
            string saved = File.ReadAllText(path);
            applied.Candidate.Instruments.Clear();
            Assert.Equal(saved, SongSerializer.Serialize(session.Song!));
            Assert.Equal(1, session.History.UndoCount);
            Assert.True(session.Undo());
            Assert.Equal(originalJson, File.ReadAllText(path));
            Assert.True(session.Redo());
            Assert.Equal(saved, File.ReadAllText(path));
        }

        /// <summary>保存先のI/O失敗を、候補が成功しても公開済み状態へ反映しない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InputOutputFailureDoesNotPublishCandidate(bool mutate)
        {
            using var fixture = new CliConversionFixture();
            string path = fixture.PathFor("source.json");
            var session = CreateSession(path);
            string original = SongSerializer.Serialize(session.Song!);
            File.Delete(path);
            Directory.CreateDirectory(path);
            Exception? exception = Record.Exception(() => Apply(session, mutate));
            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.Equal(original, SongSerializer.Serialize(session.Song!));
            Assert.Equal(0, session.History.UndoCount);
            Assert.Equal(0, session.History.RedoCount);
        }

        private static EditSession CreateSession(string path)
        {
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(SfxPresetKind.Jump, ChipKind.Nes);
            SongSerializer.Save(SfxEditor.CreateCandidate(preset.Parameters, ChipKind.Nes, "乱数操作", preset.Name).Song, path);
            var session = new EditSession();
            session.Open(path);
            return session;
        }

        private static SfxEditResult Apply(EditSession session, bool mutate, bool dryRun = false)
        {
            return mutate ? session.Sfx.Mutate(1, dryRun: dryRun) : session.Sfx.Randomize("hit", 1, dryRun: dryRun);
        }
    }
}
