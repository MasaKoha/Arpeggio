using Arpeggio.Core.Document;
using Xunit;
using Arpeggio.Daw.Presenters.PianoRoll;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>起動時のピアノロール初期表示位置を検証する。</summary>
    public sealed class PianoRollInitialPitchTests
    {
        [Fact]
        public void EmptySongShowsC6()
        {
            Assert.Equal(84, PianoRollPresenter.GetInitialTopPitch(SongFactory.Create(ChipKind.Nes)));
        }

        [Fact]
        public void HighestNoteAcrossTracksPlusMarginBecomesTop()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[0].Notes.Add(new Note { Tick = 0, DurationTicks = 12, MidiNote = 60 });
            song.Tracks[2].Notes.Add(new Note { Tick = 0, DurationTicks = 12, MidiNote = 48 });
            Assert.Equal(63, PianoRollPresenter.GetInitialTopPitch(song));
        }

        [Fact]
        public void TopIsClampedToMaximumMidiNote()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[0].Notes.Add(new Note { Tick = 0, DurationTicks = 12, MidiNote = 127 });
            Assert.Equal(127, PianoRollPresenter.GetInitialTopPitch(song));
        }
    }
}
