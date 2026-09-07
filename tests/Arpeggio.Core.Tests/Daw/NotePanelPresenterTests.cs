using System;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>効果編集の履歴・不正入力・リアルタイム公開を検証する。</summary>
    public sealed class NotePanelPresenterTests
    {
        private const int BufferFrames = 512;

        /// <summary>追加・種類と値の変更・削除が、それぞれ一履歴で戻せる。</summary>
        [Fact]
        public void AddUpdateRemoveEachRecordOneHistoryEntry()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            NotePanelPresenter presenter = fixture.Presenter.Notes;
            int initialHistory = fixture.Document.Session.History.UndoCount;

            presenter.AddEffect(NoteEffectKind.PitchSlide, 2);
            Assert.Equal(initialHistory + 1, fixture.Document.Session.History.UndoCount);
            Assert.Equal(new NoteEffect(NoteEffectKind.PitchSlide, 2), Assert.Single(presenter.SelectedNote!.Effects));
            presenter.UpdateEffect(0, NoteEffectKind.Arpeggio, NotePanelPresenter.PackArpeggio(4, 7));
            Assert.Equal(initialHistory + 2, fixture.Document.Session.History.UndoCount);
            Assert.Equal(new NoteEffect(NoteEffectKind.Arpeggio, 0x47), Assert.Single(presenter.SelectedNote!.Effects));
            presenter.RemoveEffect(0);
            Assert.Equal(initialHistory + 3, fixture.Document.Session.History.UndoCount);
            Assert.Empty(presenter.SelectedNote!.Effects);

            fixture.Presenter.Undo();
            Note restored = Assert.Single(fixture.Document.Song.Tracks[0].Notes);
            Assert.Equal(new NoteEffect(NoteEffectKind.Arpeggio, 0x47), Assert.Single(restored.Effects));
            Assert.Equal(0, restored.Tick);
            Assert.Equal(60, restored.MidiNote);
            Assert.Equal(24, restored.DurationTicks);
            Assert.Equal(15, restored.Volume);
            Assert.Equal(1, restored.InstrumentId);
            fixture.Presenter.Undo();
            Assert.Equal(NoteEffectKind.PitchSlide, Assert.Single(fixture.Document.Song.Tracks[0].Notes[0].Effects).Kind);
            fixture.Presenter.Undo();
            Assert.Empty(fixture.Document.Song.Tracks[0].Notes[0].Effects);
        }

        /// <summary>全種類を公開でき、独立した入力配列が元ノートを変更しない。</summary>
        [Theory]
        [InlineData(NoteEffectKind.PitchSlide, -2)]
        [InlineData(NoteEffectKind.Vibrato, 1)]
        [InlineData(NoteEffectKind.VolumeSlide, -1)]
        [InlineData(NoteEffectKind.Arpeggio, 0x47)]
        [InlineData(NoteEffectKind.Delay, 2)]
        public void EverySupportedKindPreservesOriginalSnapshot(NoteEffectKind kind, int value)
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            Note original = fixture.Presenter.Notes.SelectedNote!;
            fixture.Presenter.Notes.AddEffect(kind, value);
            Assert.Empty(original.Effects);
            Assert.Equal(new NoteEffect(kind, value), Assert.Single(fixture.Presenter.Notes.SelectedNote!.Effects));
            Assert.Empty(SongSerializer.Load(fixture.Path).Tracks[0].Notes);
        }

        /// <summary>重複した種類や範囲外の値を拒否して履歴を維持する。</summary>
        [Fact]
        public void InvalidEffectsPreserveSongAndHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.Presenter.Notes.AddEffect(NoteEffectKind.Arpeggio, 0x47);
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;
            Assert.Throws<SongValidationException>(() => fixture.Presenter.Notes.AddEffect(NoteEffectKind.Arpeggio, 0));
            Assert.Throws<SongValidationException>(() => fixture.Presenter.Notes.UpdateEffect(0, NoteEffectKind.Arpeggio, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Presenter.Notes.RemoveEffect(1));
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
        }

        /// <summary>二つの 4 bit 半音差を畳み、範囲外は拒否する。</summary>
        [Theory]
        [InlineData(0, 0, 0x00)]
        [InlineData(4, 7, 0x47)]
        [InlineData(15, 15, 0xff)]
        public void ArpeggioRoundTripsSemitoneInputs(int first, int second, int packed)
        {
            Assert.Equal(packed, NotePanelPresenter.PackArpeggio(first, second));
            Assert.Equal(first, NotePanelPresenter.FirstArpeggioSemitones(packed));
            Assert.Equal(second, NotePanelPresenter.SecondArpeggioSemitones(packed));
            Assert.Throws<ArgumentOutOfRangeException>(() => NotePanelPresenter.PackArpeggio(-1, second));
            Assert.Throws<ArgumentOutOfRangeException>(() => NotePanelPresenter.PackArpeggio(first, 16));
        }

        /// <summary>発音遅延の追加が出力再起動なしに次のバッファへ反映される。</summary>
        [Fact]
        public void EffectChangeReachesNextBufferWithoutRestart()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            fixture.Presenter.PianoRoll.Add(0, 60);
            fixture.Presenter.Transport.TogglePlayback();
            Assert.Contains(fixture.Audio.RequestSamples(BufferFrames), sample => sample != 0f);
            int starts = fixture.Audio.StartCount;
            int stops = fixture.Audio.StopCount;
            fixture.Presenter.Notes.AddEffect(NoteEffectKind.Delay, 12);
            Assert.All(fixture.Audio.RequestSamples(BufferFrames), sample => Assert.Equal(0f, sample));
            Assert.Equal(starts, fixture.Audio.StartCount);
            Assert.Equal(stops, fixture.Audio.StopCount);
        }
    }
}
