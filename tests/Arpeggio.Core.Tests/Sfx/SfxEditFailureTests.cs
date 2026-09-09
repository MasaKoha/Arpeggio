using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>予行・同値・編集拒否・I/O 失敗が定義、生成列、履歴へ部分反映されないことを検証する。</summary>
    public sealed class SfxEditFailureTests
    {
        private const string ChangedPatch = "{\"tone\":{\"baseFrequencyHz\":880}}";

        /// <summary>同値 patch と同期済み再生成はファイルを再保存せず、redo と公開参照を維持する。</summary>
        [Fact]
        public void EquivalentOperationsKeepHistoryProvenanceAndFileTimestamp()
        {
            using var fixture = new SfxEditFixture();
            fixture.Session.Sfx.Tweak(ChangedPatch);
            fixture.Session.Undo();
            Song song = fixture.Song;
            SfxDefinition definition = song.Sfx!;
            var instruments = song.Instruments;
            string before = SongSerializer.Serialize(song);
            string redo = SongSerializer.Serialize(fixture.Session.History.GetRedoSnapshots()[0]);
            DateTime timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(fixture.Path, timestamp);
            SfxEditResult same = fixture.Session.Sfx.Tweak("{\"tone\":{\"baseFrequencyHz\":440.0000001}}");
            SfxEditResult regenerated = fixture.Session.Sfx.Regenerate(true);
            Assert.False(same.Changed);
            Assert.False(regenerated.Changed);
            Assert.Equal(same.Revision, same.CandidateRevision);
            Assert.Same(definition, song.Sfx);
            Assert.Same(instruments, song.Instruments);
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(fixture.Path));
            Assert.Equal(before, File.ReadAllText(fixture.Path));
            Assert.Equal(0, fixture.Session.History.UndoCount);
            Assert.Equal(redo, SongSerializer.Serialize(fixture.Session.History.GetRedoSnapshots()[0]));
        }

        /// <summary>dry-run は候補値と診断を返すが、保存内容・参照・履歴を変更しない。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public void DryRunReturnsCandidateWithoutPublishing(string operation)
        {
            using var fixture = new SfxEditFixture();
            if (operation == "regenerate")
            {
                fixture.Session.Notes.SetVolume(0, 0, 4);
            }
            Song current = fixture.Song;
            SfxDefinition definition = current.Sfx!;
            byte[] file = File.ReadAllBytes(fixture.Path);
            string before = SongSerializer.Serialize(current);
            int historyCount = fixture.Session.History.UndoCount;
            string revision = SfxHash.ComputeRevision(current);
            SfxEditResult result = operation switch
            {
                "tweak" => fixture.Session.Sfx.Tweak(ChangedPatch, revision, dryRun: true),
                "regenerate" => fixture.Session.Sfx.Regenerate(true, revision, dryRun: true),
                _ => fixture.Session.Sfx.Detach(revision, dryRun: true)
            };
            Assert.True(result.Changed);
            Assert.True(result.DryRun);
            Assert.Equal(revision, result.Revision);
            Assert.NotEqual(revision, result.CandidateRevision);
            Assert.Equal(SfxHash.ComputeRevision(result.Candidate), result.CandidateRevision);
            Assert.NotSame(current, result.Candidate);
            Assert.Same(current, fixture.Song);
            Assert.Same(definition, fixture.Song.Sfx);
            Assert.Equal(before, SongSerializer.Serialize(current));
            Assert.Equal(file, File.ReadAllBytes(fixture.Path));
            Assert.Equal(historyCount, fixture.Session.History.UndoCount);
            Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
        }

        /// <summary>複数項目の途中不正や生成前制約違反で保存・履歴を増やさない。</summary>
        [Theory]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":880,\"envelope\":{\"volume\":16}}}")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":880},\"unknown\":1}")]
        [InlineData("{\"tone\":{\"enabled\":false},\"noise\":{\"enabled\":false}}")]
        [InlineData("{\"tone\":{\"envelope\":{\"sustainSeconds\":0,\"punch\":1}}}")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":null}}")]
        [InlineData("{\"tone\":{\"baseFrequencyHz\":880,\"baseFrequencyHz\":440}}")]
        public void InvalidPatchDoesNotPartiallyCommit(string patch)
        {
            using var fixture = new SfxEditFixture();
            fixture.Session.Sfx.Tweak(ChangedPatch);
            fixture.Session.Undo();
            string before = SongSerializer.Serialize(fixture.Song);
            string redo = SongSerializer.Serialize(fixture.Session.History.GetRedoSnapshots()[0]);
            Assert.Throws<SfxParameterException>(() => fixture.Session.Sfx.Tweak(patch));
            AssertUnchanged(fixture, before, 0, 1);
            Assert.Equal(redo, SongSerializer.Serialize(fixture.Session.History.GetRedoSnapshots()[0]));
            Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
        }

        /// <summary>revision は title・定義の出自も含み、同値と dry-run でも不一致を拒否する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StaleRevisionRejectsEveryOperation(bool dryRun)
        {
            using var fixture = new SfxEditFixture();
            string stale = SfxHash.ComputeRevision(fixture.Song);
            fixture.Song.Title = "別のタイトル";
            fixture.Session.Save();
            string before = SongSerializer.Serialize(fixture.Song);
            AssertConflict(() => fixture.Session.Sfx.Tweak("{\"tone\":{\"baseFrequencyHz\":440}}", stale, dryRun));
            AssertConflict(() => fixture.Session.Sfx.Regenerate(true, stale, dryRun));
            AssertConflict(() => fixture.Session.Sfx.Detach(stale, dryRun));
            AssertUnchanged(fixture, before, 0, 0);
        }

        /// <summary>読取後の外部ファイル更新を、expectedRevision が未指定でも保存直前に拒否する。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        public void ExternalFileRevisionConflictPreservesBothVersions(string operation)
        {
            using var fixture = new SfxEditFixture();
            if (operation == "regenerate")
            {
                fixture.Session.Notes.SetVolume(0, 0, 4);
            }
            string before = SongSerializer.Serialize(fixture.Song);
            int undoCount = fixture.Session.History.UndoCount;
            Song external = SongSerializer.Load(fixture.Path);
            SfxDefinitionData definition = external.Sfx!.Known!;
            external.Sfx = new SfxDefinition(definition with { SourcePreset = "laser" });
            SongSerializer.Save(external, fixture.Path);
            byte[] externalBytes = File.ReadAllBytes(fixture.Path);
            AssertConflict(() => Apply(fixture, operation));
            Assert.Equal(before, SongSerializer.Serialize(fixture.Song));
            Assert.Equal(externalBytes, File.ReadAllBytes(fixture.Path));
            Assert.Equal(undoCount, fixture.Session.History.UndoCount);
            Assert.Equal(0, fixture.Session.History.RedoCount);
            Assert.Single(Directory.GetFiles(fixture.DirectoryPath));
        }

        /// <summary>通常編集で同期が外れた定義は tweak で上書きせず、再生成も明示要求を必要とする。</summary>
        [Fact]
        public void EditedContentRequiresExplicitRegeneration()
        {
            using var fixture = new SfxEditFixture();
            fixture.Session.Notes.SetVolume(0, 0, 4);
            string before = SongSerializer.Serialize(fixture.Song);
            SfxEditException exception = Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Tweak(ChangedPatch));
            Assert.Equal("GeneratedContentChanged", exception.Code);
            Assert.Throws<SfxParameterException>(() => fixture.Session.Sfx.Regenerate(false));
            AssertUnchanged(fixture, before, 1, 0);
        }

        /// <summary>保存パラメータだけの手修正は値を現在音と扱わず、同値 patch も拒否する。</summary>
        [Fact]
        public void SavedParameterMismatchCannotBeHiddenByEquivalentPatch()
        {
            Song initial = SfxEditFixture.CreateSong();
            SfxDefinitionData definition = initial.Sfx!.Known!;
            initial.Sfx = new SfxDefinition(definition with
            {
                Parameters = definition.Parameters with { Tone = definition.Parameters.Tone with { BaseFrequencyHz = 880 } }
            });
            using var fixture = new SfxEditFixture(initial: initial);
            string before = SongSerializer.Serialize(fixture.Song);
            SfxEditException exception = Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Tweak(ChangedPatch));
            Assert.Equal("SavedParametersChanged", exception.Code);
            AssertUnchanged(fixture, before, 0, 0);
        }

        /// <summary>定義なしは暗黙に接続せず、全編集操作を拒否する。</summary>
        [Fact]
        public void MissingDefinitionRejectsAllEdits()
        {
            Song song = SfxEditFixture.CreateSong();
            song.Sfx = null;
            using var fixture = new SfxEditFixture(initial: song);
            string before = SongSerializer.Serialize(fixture.Song);
            Assert.Equal("MissingDefinition", Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Tweak(ChangedPatch)).Code);
            Assert.Equal("MissingDefinition", Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Regenerate(true)).Code);
            Assert.Equal("MissingDefinition", Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Detach()).Code);
            AssertUnchanged(fixture, before, 0, 0);
        }

        /// <summary>未知 schema・generator・乱数版は通常編集と保存で保持し、detach とその履歴だけを許可する。</summary>
        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("generatorVersion")]
        [InlineData("algorithmVersion")]
        public void UnsupportedDefinitionsSurviveOrdinaryEditsAndDetachHistory(string versionProperty)
        {
            using var fixture = new SfxEditFixture(initial: SfxEditFixture.CreateUnsupportedSong(versionProperty));
            string opaque = fixture.Song.Sfx!.UnsupportedJson!.Value.GetRawText();
            fixture.Session.Notes.SetVolume(0, 0, 4);
            string before = SongSerializer.Serialize(fixture.Song);
            Assert.Equal(opaque, fixture.Song.Sfx!.UnsupportedJson!.Value.GetRawText());
            Assert.Equal("UnsupportedSfxVersion", Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Tweak(ChangedPatch)).Code);
            Assert.Equal("UnsupportedSfxVersion", Assert.Throws<SfxEditException>(() => fixture.Session.Sfx.Regenerate(true)).Code);
            AssertUnchanged(fixture, before, 1, 0);
            fixture.Session.Sfx.Detach();
            Assert.Null(fixture.Song.Sfx);
            Assert.True(fixture.Session.Undo());
            Assert.Equal(before, SongSerializer.Serialize(fixture.Song));
            Assert.Equal(opaque, SongSerializer.Load(fixture.Path).Sfx!.UnsupportedJson!.Value.GetRawText());
            Assert.True(fixture.Session.Redo());
            Assert.Null(fixture.Song.Sfx);
        }

        /// <summary>保存先消失による失敗で編集・detach・再生成・Undo/Redo の公開状態と履歴を保持する。</summary>
        [Theory]
        [InlineData("tweak")]
        [InlineData("regenerate")]
        [InlineData("detach")]
        [InlineData("undo")]
        [InlineData("redo")]
        public void InputOutputFailureDoesNotAdvanceSongOrHistory(string operation)
        {
            using var fixture = new SfxEditFixture();
            fixture.Session.Sfx.Tweak(ChangedPatch);
            if (operation == "regenerate")
            {
                fixture.Session.Notes.SetVolume(0, 0, 4);
            }
            if (operation == "redo" || operation == "tweak")
            {
                fixture.Session.Undo();
            }
            Song published = fixture.Song;
            SfxDefinition definition = published.Sfx!;
            string before = SongSerializer.Serialize(published);
            int undoCount = fixture.Session.History.UndoCount;
            int redoCount = fixture.Session.History.RedoCount;
            Directory.Delete(fixture.DirectoryPath, true);
            Assert.ThrowsAny<IOException>(() => Apply(fixture, operation));
            Assert.Same(published, fixture.Song);
            Assert.Same(definition, fixture.Song.Sfx);
            Assert.Equal(before, SongSerializer.Serialize(fixture.Song));
            Assert.Equal(undoCount, fixture.Session.History.UndoCount);
            Assert.Equal(redoCount, fixture.Session.History.RedoCount);
            Assert.False(Directory.Exists(fixture.DirectoryPath));
        }

        private static void Apply(SfxEditFixture fixture, string operation)
        {
            switch (operation)
            {
                case "tweak": fixture.Session.Sfx.Tweak(ChangedPatch); break;
                case "regenerate": fixture.Session.Sfx.Regenerate(true); break;
                case "detach": fixture.Session.Sfx.Detach(); break;
                case "undo": fixture.Session.Undo(); break;
                case "redo": fixture.Session.Redo(); break;
                default: throw new ArgumentException("未定義の操作です。", nameof(operation));
            }
        }

        private static void AssertConflict(Action action)
        {
            Assert.Equal("RevisionConflict", Assert.Throws<SfxEditException>(action).Code);
        }

        private static void AssertUnchanged(SfxEditFixture fixture, string snapshot, int undoCount, int redoCount)
        {
            Assert.Equal(snapshot, SongSerializer.Serialize(fixture.Song));
            Assert.Equal(snapshot, File.ReadAllText(fixture.Path));
            Assert.Equal(undoCount, fixture.Session.History.UndoCount);
            Assert.Equal(redoCount, fixture.Session.History.RedoCount);
        }
    }
}
