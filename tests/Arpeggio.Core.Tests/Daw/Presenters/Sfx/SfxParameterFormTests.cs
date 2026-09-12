using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Daw.Editing.Sfx;
using Arpeggio.Daw.Presenters.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>画面からの未確定入力と確定・取消・遅延の境界を守る。</summary>
    public sealed class SfxParameterFormTests
    {
        /// <summary>別欄を編集・フォーカス移動しても不正欄を適用済みに見せない。</summary>
        [Fact]
        public void InvalidFieldSurvivesAnotherFieldAndFocusUntilCorrected()
        {
            using var fixture = new DawPresenterFixture();
            SfxEditorPresenter editor = fixture.Presenter.SfxEditor;
            editor.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            editor.AutoPreview = false;
            using var form = new SfxParameterForm(editor, new HistoricalScheduler());
            form.EditText("tone.baseFrequencyHz", "bad");
            editor.Commit();
            editor.BeginGesture();
            Assert.Equal(SfxEditingState.Invalid, editor.Model.State);
            form.EditText("tone.envelope.volume", "13");
            Assert.False(editor.Commit());
            Assert.Equal("bad", form.Text("tone.baseFrequencyHz", 196.0));
            Assert.Equal(0, editor.Model.UndoCount);
            form.EditText("tone.baseFrequencyHz", "880");
            Assert.True(editor.Commit());
            Assert.Equal(880, editor.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
            Assert.Equal(13, editor.Model.Synchronization.Parameters!.Tone.Envelope.Volume);
            Assert.Equal(1, editor.Model.UndoCount);
            editor.Undo();
            Assert.Equal(196, editor.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
            Assert.Equal(12, editor.Model.Synchronization.Parameters!.Tone.Envelope.Volume);
        }

        /// <summary>キーの旧タイマーが、次に始めた長いドラッグを途中確定しない。</summary>
        [Fact]
        public void ExplicitCommitCancelsOldTimerBeforeNextGesture()
        {
            using var fixture = new DawPresenterFixture();
            SfxEditorPresenter editor = fixture.Presenter.SfxEditor;
            editor.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            var scheduler = new HistoricalScheduler();
            using var form = new SfxParameterForm(editor, scheduler);
            int previews = 0;
            using IDisposable subscription = editor.PreviewRequests.Subscribe(_ => previews++);
            form.QueueValue("tone.baseFrequencyHz", 440.0);
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));
            editor.Commit();
            editor.BeginGesture();
            form.Drag("tone.baseFrequencyHz", 880.0);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.True(editor.Model.HasGesture);
            Assert.Equal(1, editor.Model.UndoCount);
            Assert.Equal(1, previews);
            editor.Commit();
            Assert.Equal(2, editor.Model.UndoCount);
            Assert.Equal(2, previews);
        }

        /// <summary>一連のキー変更は最後の150ms後だけ確定し、画面破棄で保留確定を止める。</summary>
        [Fact]
        public void KeyboardChangesCoalesceAndDisposedFormDoesNotCommitLater()
        {
            using var fixture = new DawPresenterFixture();
            SfxEditorPresenter editor = fixture.Presenter.SfxEditor;
            editor.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            editor.AutoPreview = false;
            var scheduler = new HistoricalScheduler();
            using var form = new SfxParameterForm(editor, scheduler);
            form.QueueValue("tone.baseFrequencyHz", 440.0);
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(149));
            form.QueueValue("tone.baseFrequencyHz", 880.0);
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(149));
            Assert.Equal(0, editor.Model.UndoCount);
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, editor.Model.UndoCount);
            form.QueueValue("tone.baseFrequencyHz", 220.0);
            form.Dispose();
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.Equal(1, editor.Model.UndoCount);
        }

        /// <summary>取消後に新しい欄を編集しても破棄した文字列が混入しない。</summary>
        [Fact]
        public void CancelClearsPendingTextBeforeNextOperation()
        {
            using var fixture = new DawPresenterFixture();
            SfxEditorPresenter editor = fixture.Presenter.SfxEditor;
            editor.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            editor.AutoPreview = false;
            using var form = new SfxParameterForm(editor, new HistoricalScheduler());
            form.EditText("tone.baseFrequencyHz", "invalid");
            editor.Cancel();
            form.Select("tone.envelope.volume", 13.0);
            Assert.Equal(SfxEditingState.None, editor.Model.State);
            Assert.Equal(196, editor.Model.Synchronization.Parameters!.Tone.BaseFrequencyHz);
            Assert.Equal(13, editor.Model.Synchronization.Parameters!.Tone.Envelope.Volume);
            Assert.Equal(1, editor.Model.UndoCount);
        }
    }
}
