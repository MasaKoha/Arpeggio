using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Synthesis.GameBoy;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Synthesis.Snes;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>チップとチャンネルの組に対応する合成器を構築する。</summary>
    public static class ChannelSynthesizerFactory
    {
        /// <summary>規定構成のチャンネルを作り、不正な組合せを拒否する。</summary>
        public static IChannelSynthesizer Create(ChipKind chip, ChannelKind channel, int sampleRate = 44100)
        {
            return (chip, channel) switch
            {
                (ChipKind.Nes, ChannelKind.Pulse) => new NesPulseSynthesizer(sampleRate),
                (ChipKind.Nes, ChannelKind.Triangle) => new NesTriangleSynthesizer(sampleRate),
                (ChipKind.Nes, ChannelKind.Noise) => new NesNoiseSynthesizer(sampleRate),
                (ChipKind.Nes, ChannelKind.Dpcm) => new NesDpcmSynthesizer(sampleRate),
                (ChipKind.GameBoy, ChannelKind.Pulse) => new GbPulseSynthesizer(sampleRate),
                (ChipKind.GameBoy, ChannelKind.Wave) => new GbWaveSynthesizer(sampleRate),
                (ChipKind.GameBoy, ChannelKind.Noise) => new GbNoiseSynthesizer(sampleRate),
                (ChipKind.Snes, ChannelKind.Sample) => new SnesVoiceSynthesizer(sampleRate),
                _ => throw new ArgumentException("チップとチャンネルの組合せが不正です。")
            };
        }
    }
}
