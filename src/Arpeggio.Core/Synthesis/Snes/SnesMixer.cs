using System;

namespace Arpeggio.Core.Synthesis.Snes
{
    /// <summary>SNES の左右 8 bit 音量と 16 bit マスター出力。</summary>
    public static class SnesMixer
    {
        private const int VoiceCount = 8;
        private const int MaximumVolume = 127;
        private const int VolumeScale = 128;
        private const double PcmScale = 32768.0;

        /// <summary>音色とトラックの合成定位を量子化して一ボイスを加算する。</summary>
        public static void Add(float sample, double pan, ref float left, ref float right)
        {
            // perf: 音量レジスタへの変換も値型だけで完結する。
            int leftVolume = (int)Math.Round(Math.Clamp(1 - pan, 0, 1) * MaximumVolume);
            int rightVolume = (int)Math.Round(Math.Clamp(1 + pan, 0, 1) * MaximumVolume);
            left += sample * leftVolume / (VolumeScale * VoiceCount);
            right += sample * rightVolume / (VolumeScale * VoiceCount);
        }

        /// <summary>正規化音声を signed 16 bit の格子と範囲へ丸める。</summary>
        public static float Clamp16(double sample)
            => (float)(Math.Clamp(Math.Round(sample * PcmScale), short.MinValue, short.MaxValue) / PcmScale);
    }
}
