using System.IO;
using System.Text;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Formats;
using Arpeggio.Daw.Presenters;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Core.Tests.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>MIDI 候補と保存内容の一致、現文書・履歴の保護、独立 Open を検証する。</summary>
    public sealed class MidiImportPresenterTests
    {
        /// <summary>三チップの候補は共通 Import と一致し、確認・新規保存で現文書と undo / redo を変更しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public async Task PrepareAndSavePreserveCurrentDocumentAndHistory(ChipKind chip)
        {
            using var fixture = new MidiImportDawFixture();
            fixture.CreateUndoAndRedo();
            Song current = fixture.Editor.Document.Song;
            string snapshot = SongSerializer.Serialize(current);
            byte[] original = File.ReadAllBytes(fixture.Editor.Path);
            byte[] source = File.ReadAllBytes(fixture.SourcePath);
            int undo = fixture.Editor.Document.Session.History.UndoCount;
            int redo = fixture.Editor.Document.Session.History.RedoCount;
            MidiImportResult expected = MidiImporterTests.Import(source, new MidiImportOptions { Chip = chip, SourceName = fixture.SourcePath });

            await fixture.Import.PrepareAsync(fixture.Input(chip));
            Assert.Equal(expected.Json, fixture.RequireCandidate().Json);
            Assert.True(fixture.Import.CanSave);
            Assert.False(fixture.Import.CanOpen);
            Assert.False(File.Exists(fixture.DestinationPath));
            Assert.Contains("採用 1", fixture.Import.ReportText);
            Assert.Contains("ch 1 → track 0: 1 音", fixture.Import.ReportText);
            Assert.Contains("channel=1", fixture.Import.ReportText);
            await fixture.Import.SaveAsync();

            Assert.Equal(Encoding.UTF8.GetBytes(expected.Json!), File.ReadAllBytes(fixture.DestinationPath));
            Assert.Equal(expected.Report.OutputBytes, new FileInfo(fixture.DestinationPath).Length);
            Assert.Equal(source, File.ReadAllBytes(fixture.SourcePath));
            Assert.Equal(original, File.ReadAllBytes(fixture.Editor.Path));
            Assert.Same(current, fixture.Editor.Document.Song);
            Assert.Equal(snapshot, SongSerializer.Serialize(current));
            Assert.Equal(undo, fixture.Editor.Document.Session.History.UndoCount);
            Assert.Equal(redo, fixture.Editor.Document.Session.History.RedoCount);
            Assert.True(fixture.Editor.Presenter.IsDirty);
            Assert.Equal(fixture.Editor.Path, fixture.Editor.View.DocumentPath);
            Assert.True(fixture.Import.CanOpen);
            Assert.False(fixture.Import.CanSave);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>入力ファイル・候補 Song・現文書の後編集を保存へ混ぜず、同じ結果だけを保存する。</summary>
        [Fact]
        public async Task SaveUsesPreparedJsonWithoutReimporting()
        {
            using var fixture = new MidiImportDawFixture();
            await fixture.Import.PrepareAsync(fixture.Input());
            MidiImportResult candidate = fixture.RequireCandidate();
            string json = candidate.Json!;
            candidate.Song!.Tracks.Clear();
            File.WriteAllText(fixture.SourcePath, "後から壊れた入力");
            fixture.Editor.Presenter.PianoRoll.Add(0, 72);
            await fixture.Import.SaveAsync();
            Assert.Same(candidate, fixture.Import.PreparedResult);
            Assert.Equal(json, File.ReadAllText(fixture.DestinationPath));
            Assert.True(fixture.Editor.Presenter.IsDirty);
            Assert.Single(fixture.Editor.Document.Song.Tracks[0].Notes);
        }

        /// <summary>保存後も自動で開かず、未保存編集を明示保存してから別操作の Open で履歴を切り替える。</summary>
        [Fact]
        public async Task OpenRequiresExplicitResolutionOfUnsavedEdits()
        {
            using var fixture = new MidiImportDawFixture();
            fixture.CreateUndoAndRedo();
            byte[] original = File.ReadAllBytes(fixture.Editor.Path);
            await fixture.Import.PrepareAsync(fixture.Input(ChipKind.GameBoy));
            await fixture.Import.SaveAsync();
            string imported = File.ReadAllText(fixture.DestinationPath);
            fixture.Import.OpenSaved();
            Assert.Equal(fixture.Editor.Path, fixture.Editor.Document.Path);
            Assert.Equal(original, File.ReadAllBytes(fixture.Editor.Path));
            Assert.Equal(1, fixture.Editor.Document.Session.History.UndoCount);
            Assert.Equal(1, fixture.Editor.Document.Session.History.RedoCount);
            Assert.Contains("現在の編集を保存", fixture.Import.StatusText);
            Assert.Equal(fixture.DestinationPath, fixture.Import.SavedPath);

            fixture.Editor.Presenter.Save();
            fixture.Import.OpenSaved();
            Assert.Equal(fixture.DestinationPath, fixture.Editor.Document.Path);
            Assert.Equal(fixture.DestinationPath, fixture.Editor.View.DocumentPath);
            Assert.Equal(imported, SongSerializer.Serialize(fixture.Editor.Document.Song));
            Assert.Equal(0, fixture.Editor.Document.Session.History.UndoCount);
            Assert.Equal(0, fixture.Editor.Document.Session.History.RedoCount);
            Assert.False(fixture.Editor.Presenter.IsDirty);
        }

        /// <summary>監視通知の前後とも外部変更を保護し、未保存内容も取り込み成功ファイルも保持する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OpenRejectsExternalConflict(bool notifyWatcher)
        {
            using var fixture = new MidiImportDawFixture();
            fixture.CreateUndoAndRedo();
            await fixture.Import.PrepareAsync(fixture.Input());
            await fixture.Import.SaveAsync();
            Song current = fixture.Editor.Document.Song;
            fixture.Editor.AddExternalNote(96, 67);
            byte[] external = File.ReadAllBytes(fixture.Editor.Path);
            byte[] imported = File.ReadAllBytes(fixture.DestinationPath);
            if (notifyWatcher) { fixture.Editor.Presenter.ExternalFileChanged(); }
            fixture.Import.OpenSaved();
            Assert.Same(current, fixture.Editor.Document.Song);
            Assert.True(fixture.Editor.Presenter.HasPendingExternalChange);
            Assert.Contains("外部変更", fixture.Import.StatusText);
            Assert.Equal(external, File.ReadAllBytes(fixture.Editor.Path));
            Assert.Equal(imported, File.ReadAllBytes(fixture.DestinationPath));
            Assert.True(fixture.Import.CanOpen);
        }

        /// <summary>Open の読み取り失敗は保存失敗と混同せず、現文書・履歴と再試行先を保持する。</summary>
        [Fact]
        public async Task FailedOpenKeepsSuccessfulSaveSeparate()
        {
            using var fixture = new MidiImportDawFixture();
            await fixture.Import.PrepareAsync(fixture.Input());
            await fixture.Import.SaveAsync();
            string json = File.ReadAllText(fixture.DestinationPath);
            Song current = fixture.Editor.Document.Song;
            File.WriteAllText(fixture.DestinationPath, "invalid JSON");
            fixture.Import.OpenSaved();
            Assert.Same(current, fixture.Editor.Document.Song);
            Assert.Equal(fixture.Editor.Path, fixture.Editor.Document.Path);
            Assert.Contains("新規保存は完了しています", fixture.Import.StatusText);
            Assert.Equal("invalid JSON", File.ReadAllText(fixture.DestinationPath));
            Assert.True(fixture.Import.CanOpen);
            File.WriteAllText(fixture.DestinationPath, json);
            fixture.Import.OpenSaved();
            Assert.Equal(fixture.DestinationPath, fixture.Editor.Document.Path);
        }

        /// <summary>strict の候補・全診断と予定サイズは表示し、警告があれば保存も Open も許可しない。</summary>
        [Fact]
        public async Task StrictShowsCandidateAndDiagnosticsButNeverSaves()
        {
            using var fixture = new MidiImportDawFixture();
            await fixture.Import.PrepareAsync(fixture.Input(strict: true));
            Assert.NotNull(fixture.RequireCandidate().Json);
            Assert.True(fixture.RequireCandidate().Report.OutputBytes > 0);
            Assert.Contains("strict", fixture.Import.ReportText);
            Assert.Contains("ProgramApproximated", fixture.Import.ReportText);
            Assert.False(fixture.Import.CanSave);
            await fixture.Import.SaveAsync();
            fixture.Import.OpenSaved();
            Assert.False(File.Exists(fixture.DestinationPath));
            Assert.False(fixture.Import.CanOpen);
        }

        /// <summary>候補確認後に保存先が作られた場合も上書きせず、同じ候補で I/O 解消後に再試行できる。</summary>
        [Fact]
        public async Task DestinationRacePreservesFileAndAllowsRetry()
        {
            using var fixture = new MidiImportDawFixture();
            await fixture.Import.PrepareAsync(fixture.Input());
            MidiImportResult candidate = fixture.RequireCandidate();
            File.WriteAllText(fixture.DestinationPath, "existing");
            await fixture.Import.SaveAsync();
            Assert.Equal("existing", File.ReadAllText(fixture.DestinationPath));
            Assert.Same(candidate, fixture.Import.PreparedResult);
            Assert.True(fixture.Import.CanSave);
            Assert.False(fixture.Import.CanOpen);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
            File.Delete(fixture.DestinationPath);
            await fixture.Import.SaveAsync();
            Assert.Equal(candidate.Json, File.ReadAllText(fixture.DestinationPath));
        }

        /// <summary>設定変更・候補破棄後は古い候補を保存せず、既に保存したファイルは削除しない。</summary>
        [Fact]
        public async Task InvalidationAndCancelClearOnlyInMemoryCandidate()
        {
            using var fixture = new MidiImportDawFixture();
            await fixture.Import.PrepareAsync(fixture.Input());
            fixture.Import.InvalidateCandidate();
            await fixture.Import.SaveAsync();
            Assert.False(File.Exists(fixture.DestinationPath));
            Assert.Null(fixture.Import.PreparedResult);
            await fixture.Import.PrepareAsync(fixture.Input());
            await fixture.Import.SaveAsync();
            string json = File.ReadAllText(fixture.DestinationPath);
            fixture.Import.Cancel();
            Assert.Null(fixture.Import.PreparedResult);
            Assert.False(fixture.Import.CanOpen);
            Assert.Equal(json, File.ReadAllText(fixture.DestinationPath));
        }
    }
}
