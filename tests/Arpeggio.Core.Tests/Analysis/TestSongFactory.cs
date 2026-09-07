using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>全チャンネルの発音遷移を含む決定的な検証ソングを作る。</summary>
    internal static class TestSongFactory
    {
        internal static Song CreateActiveSong(ChipKind chip, int lengthTicks = 768)
        {
            const int Tempo = 150;
            const int NoteSpacingTicks = 24;
            const int NoteDurationTicks = 12;
            var song = SongFactory.Create(chip, Tempo, lengthTicks);
            song.Instruments.Clear();
            if (chip == ChipKind.Snes)
            {
                song.SnesEcho = new SnesEchoSettings { DelayMilliseconds = 32, Feedback = 0.25, Volume = 0.5 };
            }
            for (int trackIndex = 0; trackIndex < song.Tracks.Count; trackIndex++)
            {
                Track track = song.Tracks[trackIndex];
                Instrument instrument = CreateInstrument(chip, track.Channel);
                instrument.Id = trackIndex + 1;
                song.Instruments.Add(instrument);
                for (int tick = 0; tick < lengthTicks; tick += NoteSpacingTicks)
                {
                    track.Notes.Add(new Note
                    {
                        Tick = tick,
                        DurationTicks = Math.Min(NoteDurationTicks, lengthTicks - tick),
                        MidiNote = SynthSamples.ReferenceNote,
                        Volume = SynthSamples.MaximumVolume,
                        InstrumentId = instrument.Id
                    });
                }
            }

            return song;
        }

        private static Instrument CreateInstrument(ChipKind chip, ChannelKind channel)
        {
            var volumeMacro = new Macro { Values = new int[] { 15, 12, 7 }, LoopIndex = 1 };
            var arpeggioMacro = new Macro { Values = new int[] { 0, 4, 7 }, LoopIndex = 0 };
            if (chip == ChipKind.Snes)
            {
                return new SnesSampleInstrument { ArpeggioMacro = arpeggioMacro, EchoSend = 0.4, Envelope = new AdsrEnvelope(0.01, 0.02, 0.7, 0.03) };
            }

            if (chip == ChipKind.GameBoy)
            {
                switch (channel)
                {
                    case ChannelKind.Pulse: return new GbPulseInstrument { VolumeMacro = volumeMacro, ArpeggioMacro = arpeggioMacro };
                    case ChannelKind.Wave: return new GbWaveInstrument { ArpeggioMacro = arpeggioMacro };
                    case ChannelKind.Noise: return new GbNoiseInstrument { VolumeMacro = volumeMacro };
                    default: throw new ArgumentOutOfRangeException(nameof(channel));
                }
            }

            switch (channel)
            {
                case ChannelKind.Pulse: return new NesPulseInstrument { VolumeMacro = volumeMacro, ArpeggioMacro = arpeggioMacro };
                case ChannelKind.Triangle: return new NesTriangleInstrument { ArpeggioMacro = arpeggioMacro };
                case ChannelKind.Noise: return new NesNoiseInstrument { VolumeMacro = volumeMacro };
                case ChannelKind.Dpcm: return new NesDpcmInstrument();
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }
    }
}
