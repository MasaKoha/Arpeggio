using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.GameBoy;

namespace Arpeggio.Core.Tests.Formats.Export.GameBoy
{
    /// <summary>GB レジスタテスト用の短い曲と、時刻ごとの書き込み観測を用意する。</summary>
    internal static class GameBoyRegisterTestData
    {
        internal const int PulseOneTrack = 0;
        internal const int PulseTwoTrack = 1;
        internal const int WaveTrack = 2;
        internal const int NoiseTrack = 3;
        internal const int WaveInstrumentId = 2;
        internal const int ConcertNote = 69;
        internal const int FullVolume = 15;
        internal const int FrameTicks = 2;
        internal const int FrameSamples = 735;

        internal static Song CreateSong(int lengthTicks = FrameTicks * 4)
        {
            Song song = SongFactory.Create(ChipKind.GameBoy, tempoBpm: 150, lengthTicks: lengthTicks);
            song.Instruments.Add(new GbWaveInstrument { Id = WaveInstrumentId });
            return song;
        }

        internal static Note AddNote(Song song, int trackIndex, int tick, int durationTicks, int midiNote = ConcertNote)
        {
            var note = new Note
            {
                Tick = tick, DurationTicks = durationTicks, MidiNote = midiNote, Volume = FullVolume,
                InstrumentId = trackIndex == WaveTrack ? WaveInstrumentId : 1
            };
            song.Tracks[trackIndex].Notes.Add(note);
            return note;
        }

        internal static ControlTimelineResult CreateControl(Song song, int loops = 1)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });

        internal static RegisterTimeline Compile(Song song, int loops = 1)
        {
            ControlTimelineResult control = CreateControl(song, loops);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.True(control.Report.CanWrite);
            Assert.NotNull(timeline);
            return timeline;
        }

        internal static (int Address, int Value)[] ValuesAt(RegisterTimeline timeline, long positionSamples)
            => timeline.Writes.Where(write => write.PositionSamples == positionSamples)
                .Select(write => ((int)write.Address, (int)write.Value)).ToArray();
    }
}
