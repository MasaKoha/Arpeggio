using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Arpeggio.Core.Tests.Brief;
using Arpeggio.Daw.Presenters.Brief;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Brief
{
    /// <summary>指示書の保存境界・入力保持・コピー完了と Song からの隔離を検証する。</summary>
    public sealed class BriefEditorPresenterTests
    {
        /// <summary>空フォームのタイトルを既定値で補い、保存とテキスト出力を可能にする。</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t ")]
        public void EmptyTitleSavesDefaultTitleAndUnspecifiedFields(string? title)
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            presenter.UpdateTitle(title);
            string path = files.PathFor("empty.brief.json");

            Assert.True(presenter.Save(path));

            CompositionBrief loaded = CompositionBriefFile.Load(path);
            Assert.Equal(CompositionBrief.DefaultTitle, loaded.Title);
            Assert.Null(loaded.Chip);
            Assert.Null(loaded.TempoBpm);
            Assert.Contains("チップ: 指定なし（AIに一任）", presenter.GetText());
            Assert.Contains("テンポ目安: 指定なし（AIに一任）", presenter.GetText());
            Assert.Equal(path, presenter.DocumentPath);
        }

        /// <summary>全自由記述の改行・前後空白を往復し、古いレコードを後続編集から保護する。</summary>
        [Fact]
        public void SaveAndOpenPreserveAllMultilineFieldsAndImmutableSnapshots()
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            presenter.UpdateTitle("廃墟の朝");
            presenter.UpdateChip(ChipKind.GameBoy);
            presenter.UpdateTempo("96");
            presenter.UpdateMood(" 寂しい\r\n希望が残る ");
            presenter.UpdateStructure("イントロ8小節\nメイン16小節");
            presenter.UpdateInstrumentation("Pulse1: 主旋律\nWave: ベース");
            presenter.UpdateReferences("参考曲A\n参考曲B");
            presenter.UpdateConstraints(" 長さ32小節\r\nループ ");
            presenter.UpdateNotes("\n自由メモ\n");
            CompositionBrief snapshot = presenter.Brief;
            string path = files.PathFor("multiline.brief.json");
            Assert.True(presenter.Save(path));
            presenter.UpdateMood("別の雰囲気");
            Assert.NotSame(snapshot, presenter.Brief);
            Assert.Equal(" 寂しい\r\n希望が残る ", snapshot.Mood);

            Assert.True(presenter.Open(path));

            Assert.Equal(snapshot, presenter.Brief);
            Assert.Equal(CompositionBriefTextRenderer.Render(snapshot), presenter.GetText());
        }

        /// <summary>読み込んだチップとテンポを指定なしへ戻しても保存後に復活しない。</summary>
        [Fact]
        public void ClearingChipAndTempoOverwritesPreviouslySavedValues()
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            string path = files.PathFor("clear.brief.json");
            CompositionBriefFile.Create(new CompositionBrief
            {
                Title = "指定あり", Chip = ChipKind.Snes, TempoBpm = 120
            }, path);
            Assert.True(presenter.Open(path));
            presenter.UpdateChip(null);
            presenter.UpdateTempo(string.Empty);

            Assert.True(presenter.Save(path));

            CompositionBrief loaded = CompositionBriefFile.Load(path);
            Assert.Null(loaded.Chip);
            Assert.Null(loaded.TempoBpm);
            Assert.Contains("指定なし（AIに一任）", presenter.GetText());
        }

        /// <summary>不正テンポは別項目の編集で隠れず、既存ファイルとクリップボードを変更しない。</summary>
        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("abc")]
        [InlineData("2147483648")]
        public async Task InvalidTempoPreventsSaveAndCopyUntilCorrected(string invalidTempo)
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            string path = files.PathFor("tempo.brief.json");
            presenter.UpdateTempo("96");
            Assert.True(presenter.Save(path));
            byte[] original = File.ReadAllBytes(path);
            presenter.UpdateTempo(invalidTempo);
            presenter.UpdateNotes("途中入力を保持");
            Assert.Contains("tempoBpm", presenter.Error);
            Assert.False(presenter.Save(path));
            bool copied = false;
            Assert.False(await presenter.CopyTextAsync(_ =>
            {
                copied = true;
                return Task.CompletedTask;
            }));
            Assert.False(copied);
            Assert.Equal(original, File.ReadAllBytes(path));

            presenter.UpdateTempo("2147483647");
            Assert.True(presenter.Save(path));
            Assert.Equal(int.MaxValue, CompositionBriefFile.Load(path).TempoBpm);
            presenter.UpdateTempo(" ");
            Assert.True(presenter.Save(path));
            Assert.Null(CompositionBriefFile.Load(path).TempoBpm);
            Assert.Empty(presenter.Error);
        }

        /// <summary>文字数超過の入力は失われず、修正後の保存でエラーから復帰する。</summary>
        [Fact]
        public void ExcessiveTextRemainsEditableAndCannotOverwriteSavedFile()
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            string path = files.PathFor("length.brief.json");
            Assert.True(presenter.Save(path));
            byte[] original = File.ReadAllBytes(path);
            string excessive = new string('あ', CompositionBriefValidator.MaximumTextLength + 1);
            presenter.UpdateMood(excessive);
            Assert.Equal(excessive, presenter.Brief.Mood);
            Assert.Contains("InvalidParameter", presenter.Error);
            Assert.Contains("mood", presenter.Error);
            Assert.False(presenter.Save(path));
            Assert.Equal(original, File.ReadAllBytes(path));
            presenter.UpdateMood(new string('あ', CompositionBriefValidator.MaximumTextLength));
            presenter.UpdateTitle(new string('題', CompositionBriefValidator.MaximumTitleLength + 1));
            Assert.Contains("title", presenter.Error);
            Assert.False(presenter.Save(path));

            presenter.UpdateTitle(new string('題', CompositionBriefValidator.MaximumTitleLength));

            Assert.True(presenter.Save(path));
            Assert.Empty(presenter.Error);
        }

        /// <summary>不正な文書を開いても編集中の指示書・途中テンポ・保存先を失わない。</summary>
        [Theory]
        [InlineData("not json")]
        [InlineData("{\"title\":\"版なし\"}")]
        [InlineData("{\"version\":1,\"title\":\"不正チップ\",\"chip\":\"None\"}")]
        public void InvalidOpenPreservesCurrentBriefAndDestination(string json)
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            string savedPath = files.PathFor("original.brief.json");
            Assert.True(presenter.Save(savedPath));
            presenter.UpdateNotes("未保存のメモ");
            presenter.UpdateTempo("途中");
            CompositionBrief before = presenter.Brief;
            string invalidPath = files.PathFor("invalid.brief.json");
            File.WriteAllText(invalidPath, json);

            Assert.False(presenter.Open(invalidPath));

            Assert.Same(before, presenter.Brief);
            Assert.Equal(savedPath, presenter.DocumentPath);
            Assert.Contains("読み込みに失敗しました", presenter.Error);
            Assert.Contains("InvalidBrief", presenter.Error);
            Assert.False(presenter.Save(savedPath));
            Assert.Contains("tempoBpm", presenter.Error);
            Assert.True(presenter.Open(savedPath));
            Assert.True(presenter.Save(savedPath));
            Assert.Empty(presenter.Error);
        }

        /// <summary>パス検証・I/O 失敗を通知し、成功済みの保存先を保持して再試行できる。</summary>
        [Fact]
        public void FileFailuresPreserveDestinationAndAllowRetry()
        {
            using var files = new BriefFileFixture();
            using var presenter = new BriefEditorPresenter();
            string originalPath = files.PathFor("original.brief.json");
            Assert.True(presenter.Save(originalPath));
            presenter.UpdateNotes("再試行する内容");
            Assert.False(presenter.Save(string.Empty));
            Assert.Contains("InvalidParameter · path", presenter.Error);
            Assert.Equal(originalPath, presenter.DocumentPath);
            Assert.False(presenter.Save(files.PathFor("missing/new.brief.json")));
            Assert.Contains("保存に失敗しました", presenter.Error);
            Assert.Equal(originalPath, presenter.DocumentPath);
            CompositionBrief before = presenter.Brief;
            Assert.False(presenter.Open(files.PathFor("missing.brief.json")));
            Assert.Same(before, presenter.Brief);
            Assert.Equal(originalPath, presenter.DocumentPath);

            string retryPath = files.PathFor("retry.brief.json");
            Assert.True(presenter.Save(retryPath));

            Assert.Equal(retryPath, presenter.DocumentPath);
            Assert.Equal("再試行する内容", CompositionBriefFile.Load(retryPath).Notes);
            Assert.Empty(presenter.Error);
            Assert.Empty(Directory.GetFiles(files.DirectoryPath, "*.tmp"));
        }

        /// <summary>コピーは整形結果を一回渡し、OS 書き込み完了後だけ成功を三秒表示する。</summary>
        [Fact]
        public async Task CopyWaitsForClipboardAndReplacesPreviousMessageTimeout()
        {
            using var presenter = new BriefEditorPresenter();
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            presenter.UpdateTitle("コピー用");
            presenter.UpdateMood("1行目\n2行目");
            var completion = new TaskCompletionSource<bool>();
            string? clipboardText = null;
            Task<bool> pending = presenter.CopyTextAsync(text =>
            {
                clipboardText = text;
                return completion.Task;
            });
            Assert.Equal(CompositionBriefTextRenderer.Render(presenter.Brief), clipboardText);
            Assert.DoesNotContain("コピーしました", messages);
            completion.SetResult(true);
            Assert.True(await pending);
            Assert.Equal("コピーしました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(2));
            Assert.True(await presenter.CopyTextAsync(_ => Task.CompletedTask));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(2));
            Assert.Equal("コピーしました", messages[^1]);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));

            Assert.Empty(messages[^1]);
        }

        /// <summary>クリップボード失敗を成功と表示せず、古い成功表示の期限でエラーを消さない。</summary>
        [Fact]
        public async Task ClipboardFailureIsVisibleAndCanBeRetried()
        {
            using var presenter = new BriefEditorPresenter();
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Assert.True(await presenter.CopyTextAsync(_ => Task.CompletedTask));

            Assert.False(await presenter.CopyTextAsync(_ => Task.FromException(new IOException("利用できません"))));

            Assert.Contains("コピーに失敗しました", presenter.Error);
            Assert.Contains("利用できません", presenter.Error);
            Assert.Empty(messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(4));
            Assert.NotEmpty(presenter.Error);
            Assert.True(await presenter.CopyTextAsync(_ => Task.CompletedTask));
            Assert.Empty(presenter.Error);
        }

        /// <summary>破棄後のコピー完了と予約済みタイマーが画面へ通知しない。</summary>
        [Fact]
        public async Task DisposeCancelsMessageTimeoutAndIgnoresPendingClipboardCompletion()
        {
            using var presenter = new BriefEditorPresenter();
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Assert.True(await presenter.CopyTextAsync(_ => Task.CompletedTask));
            presenter.Dispose();
            int countAtDispose = messages.Count;
            scheduler.AdvanceBy(TimeSpan.FromSeconds(4));
            Assert.Equal(countAtDispose, messages.Count);

            using var pendingPresenter = new BriefEditorPresenter();
            using IDisposable pendingSubscription = pendingPresenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            var completion = new TaskCompletionSource<bool>();
            Task<bool> pending = pendingPresenter.CopyTextAsync(_ => completion.Task);
            pendingPresenter.Dispose();
            countAtDispose = messages.Count;
            completion.SetResult(true);

            Assert.False(await pending);
            Assert.Equal(countAtDispose, messages.Count);
        }

        /// <summary>MainWindow の指示書編集・保存・読み込み・コピーは Song と SFX の状態を変更しない。</summary>
        [Fact]
        public async Task BriefOperationsDoNotChangeSongHistoryOrSfxCandidate()
        {
            using var fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            string songBefore = SongSerializer.Serialize(fixture.Document.Song);
            string sfxBefore = SongSerializer.Serialize(fixture.Presenter.SfxEditor.Model.Snapshot());
            int historyBefore = fixture.Document.Session.History.UndoCount;
            byte[] songFileBefore = File.ReadAllBytes(fixture.Path);
            BriefEditorPresenter presenter = fixture.Presenter.BriefEditor;
            presenter.UpdateTitle("独立した指示書");
            presenter.UpdateChip(ChipKind.Snes);
            string path = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "independent.brief.json");

            Assert.True(presenter.Save(path));
            Assert.True(presenter.Open(path));
            Assert.True(await presenter.CopyTextAsync(_ => Task.CompletedTask));

            Assert.Equal(songBefore, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(sfxBefore, SongSerializer.Serialize(fixture.Presenter.SfxEditor.Model.Snapshot()));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.Equal(songFileBefore, File.ReadAllBytes(fixture.Path));
            Assert.Equal(fixture.Path, fixture.Document.Path);
            Assert.True(fixture.Presenter.IsDirty);
        }
    }
}
