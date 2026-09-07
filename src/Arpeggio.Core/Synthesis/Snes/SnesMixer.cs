using System;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>SNES の八ボイスを左右音量へ変換する。</summary>
    public static class SnesMixer
    {
        private const int VoiceCount = 8;
        /// <summary>音色とトラックを合成した定位で一ボイスを加算する。</summary>
        public static void Add(float sample, double pan, ref float left, ref float right)
        {
            left += (float)(sample * Math.Min(1, 1 - pan) / VoiceCount);
            right += (float)(sample * Math.Min(1, 1 + pan) / VoiceCount);
        }
    }
}
