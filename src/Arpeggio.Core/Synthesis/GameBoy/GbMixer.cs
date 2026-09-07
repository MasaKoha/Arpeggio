using System;

namespace Arpeggio.Core.Synthesis.GameBoy
{
    /// <summary>GB の四チャンネルを定位付きで合成する。</summary>
    public static class GbMixer
    {
        private const int ChannelCount = 4;
        /// <summary>チャンネル音を左右へ加算し、全チャンネル分の余裕を確保する。</summary>
        public static void Add(float sample, double pan, ref float left, ref float right)
        {
            left += (float)(sample * Math.Min(1, 1 - pan) / ChannelCount);
            right += (float)(sample * Math.Min(1, 1 + pan) / ChannelCount);
        }
    }
}
