using System;

namespace Arpeggio.Core.Document
{
    /// <summary>チップが要求するチャンネル構成。</summary>
    public static class ChipLayout
    {
        /// <summary>規定の順序でチャンネル種別の独立した配列を返す。</summary>
        public static ChannelKind[] GetChannels(ChipKind chip)
        {
            switch (chip)
            {
                case ChipKind.Nes:
                    return new[] { ChannelKind.Pulse, ChannelKind.Pulse, ChannelKind.Triangle, ChannelKind.Noise, ChannelKind.Dpcm };
                case ChipKind.GameBoy:
                    return new[] { ChannelKind.Pulse, ChannelKind.Pulse, ChannelKind.Wave, ChannelKind.Noise };
                case ChipKind.Snes:
                    const int VoiceCount = 8;
                    var channels = new ChannelKind[VoiceCount];
                    Array.Fill(channels, ChannelKind.Sample);
                    return channels;
                default:
                    throw new SongValidationException("chip は Nes・GameBoy・Snes を指定してください。");
            }
        }
    }
}
