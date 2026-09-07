using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;

namespace Arpeggio.Core.Document
{
    /// <summary>チップ構成に一致する空のソングを作る。</summary>
    public static class SongFactory
    {
        /// <summary>規定トラックと既定音色を持つソングを作る。</summary>
        public static Song Create(ChipKind chip, int tempoBpm = 150, int lengthTicks = 768, SnesBankKind bank = SnesBankKind.None)
        {
            IReadOnlyList<string> presets = SnesBankLayout.Get(bank);
            if (bank != SnesBankKind.None && chip != ChipKind.Snes)
            {
                throw new SongValidationException("bank は SNES のみ指定できます。");
            }
            var song = new Song { Chip = chip, TempoBpm = tempoBpm, LengthTicks = lengthTicks };
            var channelCounts = new Dictionary<ChannelKind, int>();
            foreach (ChannelKind channel in ChipLayout.GetChannels(chip))
            {
                channelCounts.TryGetValue(channel, out int channelIndex);
                song.Tracks.Add(new Track { Channel = channel, ChannelIndex = channelIndex, Name = $"{channel} {channelIndex + 1}" });
                channelCounts[channel] = channelIndex + 1;
            }
            if (bank == SnesBankKind.None)
            {
                song.Instruments.Add(CreateDefaultInstrument(chip));
            }
            else
            {
                for (int index = 0; index < presets.Count; index++)
                {
                    int instrumentId = index + 1;
                    song.Instruments.Add(new SnesSampleInstrument { Id = instrumentId, Name = presets[index], Preset = presets[index] });
                    song.Tracks[index].Name = presets[index];
                    song.Tracks[index].DefaultInstrumentId = instrumentId;
                }
            }
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
