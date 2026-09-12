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
    /// <summary>感想の追記、保存先の追従、失敗時の保護と一時表示の寿命を検証する。</summary>
    public sealed class ListeningNotePresenterTests
    {
        private const string FeedbackSuffix = ".feedback.txt";
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
