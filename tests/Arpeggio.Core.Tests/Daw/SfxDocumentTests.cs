using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Sfx;
using Arpeggio.Daw.Editing;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>SFX 定義が作業セッションから正本保存・外部変更検出へ通ることを検証する。</summary>
    public sealed class SfxDocumentTests
    {
        private const string ChangedPatch = "{\"tone\":{\"baseFrequencyHz\":880},\"noise\":{\"enabled\":true}}";

        /// <summary>三チップとも一括編集は作業ファイルへだけ保存し、明示保存で定義と生成列が正本へ届く。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void ExplicitSavePreservesDefinitionAndHistory(ChipKind chip)
        {
            using var fixture = new SfxEditFixture(chip);
            using var document = new DawDocument();
            document.Open(fixture.Path);
            byte[] original = File.ReadAllBytes(fixture.Path);
            SfxEditResult result = document.Session.Sfx.Tweak(ChangedPatch);
            Assert.True(document.IsDirty);
            Assert.False(document.HasExternalChange());
            Assert.Equal(original, File.ReadAllBytes(fixture.Path));
            Assert.Equal(1, document.Session.History.UndoCount);
            Assert.Equal(result.Revision, SfxHash.ComputeRevision(SongSerializer.Load(document.Session.Path!)));
            document.Save();
            Assert.False(document.IsDirty);
            Assert.False(document.HasExternalChange());
            Assert.Equal(result.Revision, SfxHash.ComputeRevision(SongSerializer.Load(fixture.Path)));
            Assert.True(SfxSynchronization.Inspect(SongSerializer.Load(fixture.Path)).Editable);
            Assert.True(document.Session.Undo());
            Assert.True(document.IsDirty);
            document.Save();
            Assert.Equal(original, File.ReadAllBytes(fixture.Path));
            Assert.True(document.Session.Redo());
            document.Save();
            Assert.Equal(result.Revision, SfxHash.ComputeRevision(SongSerializer.Load(fixture.Path)));
            document.Session.Sfx.Detach();
            Assert.True(document.IsDirty);
            document.Save();
            Assert.Null(SongSerializer.Load(fixture.Path).Sfx);
            Assert.True(document.Session.Undo());
            document.Save();
            document.Open(fixture.Path);
            Assert.Equal(result.Revision, SfxHash.ComputeRevision(document.Song));
        }

        /// <summary>未知版の通常編集・正本保存・detach の Undo が不透明 JSON を失わない。</summary>
        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("generatorVersion")]
        [InlineData("algorithmVersion")]
        public void UnknownDefinitionSurvivesMasterSaveAndUndo(string versionProperty)
        {
            using var fixture = new SfxEditFixture(initial: SfxEditFixture.CreateUnsupportedSong(versionProperty));
            using var document = new DawDocument();
            document.Open(fixture.Path);
            string opaque = document.Song.Sfx!.UnsupportedJson!.Value.GetRawText();
            document.Session.Notes.SetVolume(0, 0, 4);
            document.Save();
            Assert.Equal(opaque, SongSerializer.Load(fixture.Path).Sfx!.UnsupportedJson!.Value.GetRawText());
            document.Session.Sfx.Detach();
            document.Save();
            Assert.Null(SongSerializer.Load(fixture.Path).Sfx);
            document.Session.Undo();
            document.Save();
            Assert.Equal(opaque, SongSerializer.Load(fixture.Path).Sfx!.UnsupportedJson!.Value.GetRawText());
            Assert.False(document.IsDirty);
            Assert.False(document.HasExternalChange());
        }

        /// <summary>sfx だけの外部変更も検知し、生成列不変の detach は未保存編集として扱う。</summary>
        [Fact]
        public void DefinitionOnlyChangesParticipateInDirtyAndExternalComparison()
        {
            using var fixture = new SfxEditFixture();
            using var document = new DawDocument();
            document.Open(fixture.Path);
            string generated = SfxHash.ComputeGeneratedHash(document.Song);
            Song external = SongSerializer.Load(fixture.Path);
            SfxDefinitionData definition = external.Sfx!.Known!;
            external.Sfx = new SfxDefinition(definition with { SourcePreset = "laser" });
            SongSerializer.Save(external, fixture.Path);
            Assert.True(document.HasExternalChange());
            Assert.False(document.IsDirty);
            Assert.Equal(generated, SfxHash.ComputeGeneratedHash(external));
            document.Open(fixture.Path);
            document.Session.Sfx.Detach();
            Assert.True(document.IsDirty);
            Assert.False(document.HasExternalChange());
            Assert.Equal(generated, SfxHash.ComputeGeneratedHash(document.Song));
            document.Session.Undo();
            Assert.False(document.IsDirty);
        }

        /// <summary>正本の置換失敗では保存基準と作業履歴を進めず、一時ファイルを除去して再試行できる。</summary>
        [Fact]
        public void MasterSaveFailureKeepsWorkingDefinitionAndCanRetry()
        {
            using var fixture = new SfxEditFixture();
            using var document = new DawDocument();
            document.Open(fixture.Path);
            byte[] original = File.ReadAllBytes(fixture.Path);
            document.Session.Sfx.Tweak(ChangedPatch);
            string working = SongSerializer.Serialize(document.Song);
            int savedCount = 0;
            Action<string> onSaved = _ => savedCount++;
            document.Saved += onSaved;
            try
            {
                File.Delete(fixture.Path);
                Directory.CreateDirectory(fixture.Path);
                Exception? exception = Record.Exception(document.Save);
                Assert.True(exception is IOException or UnauthorizedAccessException);
                Assert.True(document.IsDirty);
                Assert.Equal(0, savedCount);
                Assert.Equal(1, document.Session.History.UndoCount);
                Assert.Equal(working, SongSerializer.Serialize(document.Song));
                Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
                Directory.Delete(fixture.Path);
                File.WriteAllBytes(fixture.Path, original);
                document.Save();
                Assert.False(document.IsDirty);
                Assert.Equal(1, savedCount);
                Assert.Equal(working, File.ReadAllText(fixture.Path));
            }
            finally
            {
                document.Saved -= onSaved;
            }
        }

        /// <summary>既存 Presenter の保存前確認も sfx のみの外部変更で正本を保護する。</summary>
        [Fact]
        public void PresenterRejectsMasterSaveAfterDefinitionOnlyExternalChange()
        {
            using var fixture = new DawPresenterFixture();
            SongSerializer.Save(SfxEditFixture.CreateSong(), fixture.Path);
            fixture.Presenter.Open(fixture.Path);
            fixture.Document.Session.Sfx.Tweak(ChangedPatch);
            Song external = SongSerializer.Load(fixture.Path);
            external.Sfx = null;
            SongSerializer.Save(external, fixture.Path);
            byte[] expected = File.ReadAllBytes(fixture.Path);
            fixture.Presenter.Save();
            Assert.True(fixture.Presenter.HasPendingExternalChange);
            Assert.True(fixture.Document.IsDirty);
            Assert.Equal(expected, File.ReadAllBytes(fixture.Path));
            Assert.NotNull(fixture.Document.Song.Sfx);
        }
    }
}
