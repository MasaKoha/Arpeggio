using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>プリセット作成時の正本・セッション・監視対象の切替を検証する。</summary>
    public sealed class SfxCreationPresenterTests
    {
        /// <summary>カタログの既定保存先に生成し、履歴を初期化して新規文書へ切り替える。</summary>
        [Fact]
        public void CreationSwitchesSessionAndWatcherAfterSavingPreviousEdits()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            Song previousSong = fixture.Document.Song;
            string destination = fixture.Presenter.SfxCreation.DefaultPath(SfxPresetKind.Coin);
            Assert.Equal(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "sfx-coin.arpeggio.json"), destination);
            Assert.Equal(SfxPresetCatalog.GetAll(), fixture.Presenter.SfxCreation.Presets);

            fixture.Presenter.SfxCreation.Create(SfxPresetKind.Coin, destination);

            Assert.NotSame(previousSong, fixture.Document.Song);
            Assert.Equal(destination, fixture.Document.Path);
            Assert.Equal(destination, fixture.View.DocumentPath);
            Assert.Same(fixture.Document.Song, fixture.View.Song);
            Assert.Equal(ChipKind.Nes, fixture.Document.Song.Chip);
            Assert.Equal(0, fixture.Document.Session.History.UndoCount);
            Assert.False(fixture.Presenter.IsDirty);
            Assert.Single(SongSerializer.Load(fixture.Path).Tracks[0].Notes);
            Assert.Equal(SongSerializer.Serialize(SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.Coin)),
                SongSerializer.Serialize(fixture.Document.Song));
        }

        /// <summary>保存先の既存ファイルを上書きせず、現在のセッションを維持する。</summary>
        [Fact]
        public void ExistingDestinationDoesNotSwitchDocument()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            Song previousSong = fixture.Document.Song;
            string destination = fixture.Presenter.SfxCreation.DefaultPath(SfxPresetKind.Jump);
            File.WriteAllText(destination, "existing");
            fixture.Presenter.Execute(() => fixture.Presenter.SfxCreation.Create(SfxPresetKind.Jump, destination));
            Assert.Same(previousSong, fixture.Document.Song);
            Assert.Equal(fixture.Path, fixture.View.DocumentPath);
            Assert.Equal("existing", File.ReadAllText(destination));
            Assert.Contains("既に存在", fixture.View.Status);
        }

        /// <summary>外部変更と未保存編集が競合した場合は作成も切替も行わない。</summary>
        [Fact]
        public void ExternalConflictPreservesUnsavedEditsAndDoesNotCreateFile()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.AddExternalNote(96, 67);
            string destination = fixture.Presenter.SfxCreation.DefaultPath(SfxPresetKind.Jump);
            fixture.Presenter.Execute(() => fixture.Presenter.SfxCreation.Create(SfxPresetKind.Jump, destination));
            Assert.False(File.Exists(destination));
            Assert.True(fixture.Presenter.IsDirty);
            Assert.Equal(fixture.Path, fixture.Document.Path);
            Assert.Equal(0, Assert.Single(fixture.Document.Song.Tracks[0].Notes).Tick);
            Assert.Equal(96, Assert.Single(SongSerializer.Load(fixture.Path).Tracks[0].Notes).Tick);
        }
    }
}
