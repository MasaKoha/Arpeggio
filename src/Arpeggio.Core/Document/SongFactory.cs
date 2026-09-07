using System.Collections.Generic;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Document
{
    /// <summary>チップ構成に一致する空のソングを作る。</summary>
    public static class SongFactory
    {
        /// <summary>規定トラックと既定音色を持つソングを作る。</summary>
        public static Song Create(ChipKind chip, int tempoBpm = 150, int lengthTicks = 768)
        {
            var song = new Song { Chip = chip, TempoBpm = tempoBpm, LengthTicks = lengthTicks };
            var channelCounts = new Dictionary<ChannelKind, int>();
            foreach (ChannelKind channel in ChipLayout.GetChannels(chip))
            {
                channelCounts.TryGetValue(channel, out int channelIndex);
                song.Tracks.Add(new Track { Channel = channel, ChannelIndex = channelIndex, Name = $"{channel} {channelIndex + 1}" });
                channelCounts[channel] = channelIndex + 1;
            }
            song.Instruments.Add(CreateDefaultInstrument(chip));
            SongValidator.Validate(song);
            return song;
        }

        private static Instrument CreateDefaultInstrument(ChipKind chip)
        {
            switch (chip)
            {
                case ChipKind.Nes: return new NesPulseInstrument { Name = "lead" };
                case ChipKind.GameBoy: return new GbPulseInstrument { Name = "lead" };
                case ChipKind.Snes: return new SnesSampleInstrument { Name = "lead" };
                default: throw new SongValidationException("chip が不正です。");
            }
        }
    }
}
