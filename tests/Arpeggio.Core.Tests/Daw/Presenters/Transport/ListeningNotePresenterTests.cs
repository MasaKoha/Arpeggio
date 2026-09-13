using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Presenters.Transport;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Transport
{
    /// <summary>感想の追記と修正依頼の上書き、失敗時の保護と一時表示の寿命を検証する。</summary>
    public sealed class ListeningNotePresenterTests
    {
        private const string FeedbackSuffix = ".feedback.txt";
        private const string FixRequestSuffix = ".fix-request.txt";
        private const int FirstNoteTick = 0;
        private const int MiddleCMidiNote = 60;
        private const int BeforeMessageExpirySeconds = 2;
        private const int RemainingMessageSeconds = 1;
        private const int AfterMessageExpirySeconds = 4;

        /// <summary>既存メモを消さずに複数行の感想を追記し、曲・履歴・再生状態へ影響しない。</summary>
        [Fact]
        public void SaveAppendsTimestampedNotesWithoutChangingSongOrPlayback()
        {
            using var fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(FirstNoteTick, MiddleCMidiNote);
            fixture.Presenter.Transport.TogglePlayback();
            string songBefore = SongSerializer.Serialize(fixture.Document.Song);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            byte[] songFileBefore = File.ReadAllBytes(fixture.Path);
            string feedbackPath = fixture.Path + FeedbackSuffix;
            string existingNote = "以前の感想" + Environment.NewLine + Environment.NewLine;
            File.WriteAllText(feedbackPath, existingNote);
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            const string FirstNote = "  サビがうるさい\nベースは心地よい  ";
            const string SecondNote = "イントロはこのままでよい";
            DateTime beforeSave = DateTime.Now;

            Assert.True(presenter.Save(FirstNote));
            string afterFirstSave = File.ReadAllText(feedbackPath);
            Assert.True(presenter.Save(SecondNote));
            DateTime afterSave = DateTime.Now;
            string afterSecondSave = File.ReadAllText(feedbackPath);

            Assert.StartsWith(existingNote, afterFirstSave);
            Assert.StartsWith(afterFirstSave, afterSecondSave);
            AssertEntry(afterFirstSave[existingNote.Length..], FirstNote, beforeSave, afterSave);
            AssertEntry(afterSecondSave[afterFirstSave.Length..], SecondNote, beforeSave, afterSave);
            Assert.Equal(songBefore, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.Equal(songFileBefore, File.ReadAllBytes(fixture.Path));
            Assert.True(fixture.Presenter.IsDirty);
            Assert.True(fixture.Presenter.Transport.IsPlaying);
        }

        /// <summary>未保存の曲では失敗を通知し、同じ文書に保存先ができた後は再試行できる。</summary>
        [Fact]
        public void UnsavedDocumentReportsFailureAndCanSaveAfterOpeningSong()
        {
            using var fixture = new DawPresenterFixture();
            using var document = new DawDocument();
            using var presenter = new ListeningNotePresenter(document);
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);

            Assert.False(presenter.Save("サビがうるさい"));

            Assert.Contains("先に曲を保存してください", presenter.Error);
            Assert.Equal(presenter.Error, Assert.Single(messages));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(AfterMessageExpirySeconds));
            Assert.Empty(messages[^1]);
            Assert.False(File.Exists(fixture.Path + FeedbackSuffix));

            document.Open(fixture.Path);
            Assert.True(presenter.Save("サビがうるさい"));
            Assert.Contains("サビがうるさい", File.ReadAllText(fixture.Path + FeedbackSuffix));
            Assert.Empty(presenter.Error);
        }

        /// <summary>空文字と空白のみの入力では、新規ファイル作成も既存ファイルへの追記も行わない。</summary>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\r\n")]
        [InlineData("　")]
        public void BlankInputDoesNotCreateOrAppendFile(string text)
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            string feedbackPath = fixture.Path + FeedbackSuffix;

            Assert.False(presenter.Save(text));
            Assert.False(File.Exists(feedbackPath));
            Assert.True(presenter.Save("残しておく感想"));
            byte[] before = File.ReadAllBytes(feedbackPath);

            Assert.False(presenter.Save(text));

            Assert.Equal(before, File.ReadAllBytes(feedbackPath));
        }

        /// <summary>Presenter 作成後に曲を開き直すと、新しい曲の隣へ保存し旧メモを変更しない。</summary>
        [Fact]
        public void SaveUsesCurrentDocumentPathAfterSongSwitch()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            Assert.True(presenter.Save("最初の曲の感想"));
            byte[] originalFeedback = File.ReadAllBytes(fixture.Path + FeedbackSuffix);
            string nextPath = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "next.arpeggio.json");
            File.Copy(fixture.Path, nextPath);

            fixture.Presenter.Open(nextPath);
            Assert.True(presenter.Save("次の曲の感想"));

            Assert.Equal(originalFeedback, File.ReadAllBytes(fixture.Path + FeedbackSuffix));
            string nextFeedback = File.ReadAllText(nextPath + FeedbackSuffix);
            Assert.Contains("次の曲の感想", nextFeedback);
            Assert.DoesNotContain("最初の曲の感想", nextFeedback);
        }

        /// <summary>書き込みできない保存先では失敗を返し、原因を解消すると同じ本文で再試行できる。</summary>
        [Fact]
        public void FileFailureReportsReasonAndAllowsRetry()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            string feedbackPath = fixture.Path + FeedbackSuffix;
            Directory.CreateDirectory(feedbackPath);
            const string Note = "保存し直す感想";

            Assert.False(presenter.Save(Note));

            Assert.Contains("感想の保存に失敗しました:", presenter.Error);
            Assert.True(Directory.Exists(feedbackPath));
            Directory.Delete(feedbackPath);

            Assert.True(presenter.Save(Note));
            Assert.Empty(presenter.Error);
            Assert.Contains(Note, File.ReadAllText(feedbackPath));
        }

        /// <summary>連続保存と成功・失敗の切り替えで、古いタイマーが新しい結果を早く消さない。</summary>
        [Fact]
        public void NewResultReplacesPreviousMessageTimeout()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Assert.True(presenter.Save("1回目"));
            Assert.Equal("保存しました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.True(presenter.Save("2回目"));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.Equal("保存しました", messages[^1]);

            string feedbackPath = fixture.Path + FeedbackSuffix;
            File.Delete(feedbackPath);
            Directory.CreateDirectory(feedbackPath);
            Assert.False(presenter.Save("再試行する感想"));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.Equal(presenter.Error, messages[^1]);

            Directory.Delete(feedbackPath);
            Assert.True(presenter.Save("再試行する感想"));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.Equal("保存しました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(RemainingMessageSeconds));
            Assert.Empty(messages[^1]);
        }

        /// <summary>所有元の破棄で予約済み通知が止まり、その後の書き込みを拒否する。</summary>
        [Fact]
        public void DisposeCancelsPendingMessageAndPreventsFurtherWrites()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Assert.True(presenter.Save("保存済み"));
            byte[] feedbackBefore = File.ReadAllBytes(fixture.Path + FeedbackSuffix);

            fixture.Presenter.Dispose();
            int countAtDispose = messages.Count;
            scheduler.AdvanceBy(TimeSpan.FromSeconds(AfterMessageExpirySeconds));

            Assert.Equal(countAtDispose, messages.Count);
            Assert.Throws<ObjectDisposedException>(() => presenter.Save("破棄後"));
            Assert.Equal(feedbackBefore, File.ReadAllBytes(fixture.Path + FeedbackSuffix));
        }

        /// <summary>最新の依頼だけを残し、本文の空白・改行を保持して感想・曲・履歴・再生状態を変更しない。</summary>
        [Fact]
        public void RequestFixOverwritesPreviousRequestWithoutChangingFeedbackOrSong()
        {
            using var fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(FirstNoteTick, MiddleCMidiNote);
            fixture.Presenter.Transport.TogglePlayback();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            Assert.True(presenter.Save("記録だけに残す感想"));
            byte[] feedbackBefore = File.ReadAllBytes(fixture.Path + FeedbackSuffix);
            byte[] songFileBefore = File.ReadAllBytes(fixture.Path);
            string songBefore = SongSerializer.Serialize(fixture.Document.Song);
            int historyBefore = fixture.Document.Session.History.UndoCount;
            string requestPath = fixture.Path + FixRequestSuffix;
            const string FirstRequest = "  サビの音量を下げてください\nベースはこのままにしてください  ";
            const string SecondRequest = "少し遅くして";
            DateTime beforeRequest = DateTime.Now;

            Assert.True(presenter.RequestFix(FirstRequest));
            string firstEntry = File.ReadAllText(requestPath);
            Assert.True(presenter.RequestFix(SecondRequest));
            DateTime afterRequest = DateTime.Now;

            AssertFixRequestEntry(firstEntry, FirstRequest, beforeRequest, afterRequest);
            AssertFixRequestEntry(File.ReadAllText(requestPath), SecondRequest, beforeRequest, afterRequest);
            Assert.Equal(feedbackBefore, File.ReadAllBytes(fixture.Path + FeedbackSuffix));
            Assert.Equal(songFileBefore, File.ReadAllBytes(fixture.Path));
            Assert.Equal(songBefore, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(historyBefore, fixture.Document.Session.History.UndoCount);
            Assert.True(fixture.Presenter.IsDirty);
            Assert.True(fixture.Presenter.Transport.IsPlaying);
        }

        /// <summary>未保存曲の依頼はエラーを通知し、保存先ができれば感想の保存なしで依頼できる。</summary>
        [Fact]
        public void RequestFixRejectsUnsavedDocumentAndAllowsRetryAfterOpeningSong()
        {
            using var fixture = new DawPresenterFixture();
            using var document = new DawDocument();
            using var presenter = new ListeningNotePresenter(document);
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);

            Assert.False(presenter.RequestFix("サビを静かにして"));

            Assert.Contains("先に曲を保存してください", presenter.Error);
            Assert.Equal(presenter.Error, Assert.Single(messages));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(AfterMessageExpirySeconds));
            Assert.Empty(messages[^1]);
            Assert.False(File.Exists(fixture.Path + FixRequestSuffix));

            document.Open(fixture.Path);
            Assert.True(presenter.RequestFix("サビを静かにして"));
            Assert.Contains("サビを静かにして", File.ReadAllText(fixture.Path + FixRequestSuffix));
            Assert.Empty(presenter.Error);
            Assert.False(File.Exists(fixture.Path + FeedbackSuffix));
        }

        /// <summary>空白入力で依頼を新規作成せず、既存の依頼や通知も消去しない。</summary>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\r\n")]
        [InlineData("　")]
        public void RequestFixWithBlankInputDoesNotCreateOrOverwriteFile(string text)
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            string requestPath = fixture.Path + FixRequestSuffix;
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);

            Assert.False(presenter.RequestFix(text));
            Assert.False(File.Exists(requestPath));
            Assert.Empty(messages);
            Assert.True(presenter.RequestFix("残しておく修正依頼"));
            byte[] requestBefore = File.ReadAllBytes(requestPath);

            Assert.False(presenter.RequestFix(text));

            Assert.Equal(requestBefore, File.ReadAllBytes(requestPath));
            Assert.Equal("修正を依頼しました", Assert.Single(messages));
        }

        /// <summary>曲を切り替えた後の依頼は新しい曲の隣に書き出し、旧曲の依頼を保持する。</summary>
        [Fact]
        public void RequestFixUsesCurrentDocumentPathAfterSongSwitch()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            Assert.True(presenter.RequestFix("最初の曲を直して"));
            byte[] originalRequest = File.ReadAllBytes(fixture.Path + FixRequestSuffix);
            string nextPath = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "next.arpeggio.json");
            File.Copy(fixture.Path, nextPath);

            fixture.Presenter.Open(nextPath);
            Assert.True(presenter.RequestFix("次の曲を直して"));

            Assert.Equal(originalRequest, File.ReadAllBytes(fixture.Path + FixRequestSuffix));
            string nextRequest = File.ReadAllText(nextPath + FixRequestSuffix);
            Assert.Contains("次の曲を直して", nextRequest);
            Assert.DoesNotContain("最初の曲を直して", nextRequest);
        }

        /// <summary>片方の書き込みが失敗しても他方は成功し、原因の解消後は同じ本文で再試行できる。</summary>
        [Fact]
        public void SaveAndRequestFixRemainIndependentAfterFileFailures()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            string requestPath = fixture.Path + FixRequestSuffix;
            string feedbackPath = fixture.Path + FeedbackSuffix;
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Directory.CreateDirectory(requestPath);
            const string Request = "サビを直して";

            Assert.False(presenter.RequestFix(Request));
            Assert.Contains("修正依頼の書き出しに失敗しました:", presenter.Error);
            Assert.Equal(presenter.Error, messages[^1]);
            Assert.True(Directory.Exists(requestPath));
            Assert.True(presenter.Save("保存できる感想"));
            Assert.Empty(presenter.Error);
            Assert.Contains("保存できる感想", File.ReadAllText(feedbackPath));
            Directory.Delete(requestPath);
            Assert.True(presenter.RequestFix(Request));

            File.Delete(feedbackPath);
            Directory.CreateDirectory(feedbackPath);
            byte[] requestBefore = File.ReadAllBytes(requestPath);
            Assert.False(presenter.Save("再試行する感想"));
            Assert.Equal(requestBefore, File.ReadAllBytes(requestPath));
            Assert.True(presenter.RequestFix("ベースも直して"));
            Assert.Empty(presenter.Error);
            Assert.Contains("ベースも直して", File.ReadAllText(requestPath));
            Assert.True(Directory.Exists(feedbackPath));
            Directory.Delete(feedbackPath);
            requestBefore = File.ReadAllBytes(requestPath);
            Assert.True(presenter.Save("再試行する感想"));
            Assert.Equal(requestBefore, File.ReadAllBytes(requestPath));
        }

        /// <summary>保存と依頼の通知を区別し、操作の切り替えで表示期限を更新して三秒後に消す。</summary>
        [Fact]
        public void RequestFixAndSaveReplaceEachOthersMessageTimeout()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Assert.True(presenter.Save("記録する感想"));
            Assert.Equal("保存しました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));

            Assert.True(presenter.RequestFix("サビを直して"));
            Assert.Equal("修正を依頼しました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.Equal("修正を依頼しました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(RemainingMessageSeconds));
            Assert.Empty(messages[^1]);

            Assert.True(presenter.RequestFix("ベースを直して"));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.True(presenter.Save("追加の感想"));
            scheduler.AdvanceBy(TimeSpan.FromSeconds(BeforeMessageExpirySeconds));
            Assert.Equal("保存しました", messages[^1]);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(RemainingMessageSeconds));
            Assert.Empty(messages[^1]);
        }

        /// <summary>依頼の通知待ちで破棄してもタイマーが残らず、破棄後に依頼を上書きできない。</summary>
        [Fact]
        public void DisposeCancelsRequestFixMessageAndPreventsFurtherRequests()
        {
            using var fixture = new DawPresenterFixture();
            ListeningNotePresenter presenter = fixture.Presenter.ListeningNote;
            var scheduler = new HistoricalScheduler();
            var messages = new List<string>();
            using IDisposable subscription = presenter.ObserveMessages(scheduler).Subscribe(messages.Add);
            Assert.True(presenter.RequestFix("有効な依頼"));
            byte[] requestBefore = File.ReadAllBytes(fixture.Path + FixRequestSuffix);

            fixture.Presenter.Dispose();
            int countAtDispose = messages.Count;
            scheduler.AdvanceBy(TimeSpan.FromSeconds(AfterMessageExpirySeconds));

            Assert.Equal(countAtDispose, messages.Count);
            Assert.Throws<ObjectDisposedException>(() => presenter.RequestFix("破棄後の依頼"));
            Assert.Equal(requestBefore, File.ReadAllBytes(fixture.Path + FixRequestSuffix));
        }

        private static void AssertFixRequestEntry(string entry, string text, DateTime beforeRequest, DateTime afterRequest)
        {
            string pattern = @"\A\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2})\] " +
                Regex.Escape(text + Environment.NewLine) + @"\z";
            Match match = Regex.Match(entry, pattern);
            Assert.True(match.Success, $"修正依頼のタイムスタンプ・本文・末尾改行の書式が異なります: {entry}");
            DateTime timestamp = DateTime.ParseExact(match.Groups["timestamp"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            DateTime firstPossibleMinute = beforeRequest.AddTicks(-(beforeRequest.Ticks % TimeSpan.TicksPerMinute));
            Assert.InRange(timestamp, firstPossibleMinute, afterRequest);
        }

        private static void AssertEntry(string entry, string text, DateTime beforeSave, DateTime afterSave)
        {
            string pattern = @"\A\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2})\] " +
                Regex.Escape(text + Environment.NewLine + Environment.NewLine) + @"\z";
            Match match = Regex.Match(entry, pattern);
            Assert.True(match.Success, $"タイムスタンプ・本文・空行の書式が異なります: {entry}");
            DateTime timestamp = DateTime.ParseExact(match.Groups["timestamp"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            DateTime firstPossibleMinute = beforeSave.AddTicks(-(beforeSave.Ticks % TimeSpan.TicksPerMinute));
            Assert.InRange(timestamp, firstPossibleMinute, afterSave);
        }
    }
}
