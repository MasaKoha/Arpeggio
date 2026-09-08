using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>MIDI 周波数とチップの周期レジスタ制約を結び付ける。</summary>
    public static class PitchTable
    {
        private const double ConcertFrequency = 440;
        private const int ConcertNote = 69;
        private const int SemitonesPerOctave = 12;
        private const double NesClock = 1789773;
        private const int MaximumTimer = 2047;
        private const int MinimumPulseTimer = 8;
        private const double GameBoyPulseClock = 131072;
        private const double GameBoyWaveClock = 65536;
        private const double MaximumSnesRatio = 4;
        /// <summary>原速を表す SNES ピッチ値。</summary>
        public const int SnesUnityPitch = 4096;
        /// <summary>SNES の 14 bit ピッチ上限。</summary>
        public const int MaximumSnesPitch = 0x3FFF;
        /// <summary>内蔵サンプルの一周期長。32 kHz で原速 250 Hz。</summary>
        public const int SnesWaveformLength = 128;
        private const int SnesSampleRate = 32000;

        /// <summary>平均律の MIDI 番号を Hz に変換する。</summary>
        public static double GetFrequency(double midiNote) => ConcertFrequency * Math.Pow(2, (midiNote - ConcertNote) / SemitonesPerOctave);

        /// <summary>量子化前の発音可能範囲へ MIDI 音程を制限する。</summary>
        public static double ClampMidiNote(ChipKind chip, ChannelKind channel, double midiNote)
        {
            GetRange(chip, channel, out double minimum, out double maximum);
            double frequency = Math.Clamp(GetFrequency(midiNote), minimum, maximum);
            return ConcertNote + SemitonesPerOctave * Math.Log(frequency / ConcertFrequency, 2);
        }

        /// <summary>クランプと整数周期への量子化を適用した周波数を返す。</summary>
        public static double Quantize(ChipKind chip, ChannelKind channel, double midiNote)
        {
            double frequency = GetFrequency(ClampMidiNote(chip, channel, midiNote));
            if (chip == ChipKind.Nes)
            {
                double divider = channel == ChannelKind.Triangle ? 32 : 16;
                int minimum = channel == ChannelKind.Triangle ? 0 : MinimumPulseTimer;
                double timer = Math.Clamp(Math.Round(NesClock / (divider * frequency) - 1), minimum, MaximumTimer);
                return NesClock / (divider * (timer + 1));
            }
            if (chip == ChipKind.GameBoy)
            {
                double clock = channel == ChannelKind.Wave ? GameBoyWaveClock : GameBoyPulseClock;
                double period = Math.Clamp(Math.Round(clock / frequency), 1, MaximumTimer + 1);
                return clock / period;
            }
            if (chip == ChipKind.Snes)
            {
                double rootFrequency = (double)SnesSampleRate / SnesWaveformLength;
                int register = GetSnesPitchRegister(frequency / rootFrequency);
                return rootFrequency * register / SnesUnityPitch;
            }
            return frequency;
        }

        /// <summary>32 kHz の一サンプル当たりの増分を 14 bit レジスタへ四捨五入する。</summary>
        public static int GetSnesPitchRegister(double sampleStep)
            => (int)Math.Clamp(Math.Round(sampleStep * SnesUnityPitch, MidpointRounding.AwayFromZero), 0, MaximumSnesPitch);

        /// <summary>32 kHz サンプルの基準音に対する 14 bit ピッチ倍率を求める。</summary>
        public static double GetSnesSampleRatio(double midiNote, int rootMidiNote)
        {
            double ratio = Math.Pow(2, (midiNote - rootMidiNote) / SemitonesPerOctave);
            return GetSnesPitchRegister(ratio) / (double)SnesUnityPitch;
        }

        /// <summary>埋め込みサンプルのピッチ倍率上限・下限に対応する音程へ制限する。</summary>
        public static double ClampSnesSampleMidiNote(double midiNote, int rootMidiNote)
        {
            double minimum = rootMidiNote + SemitonesPerOctave * Math.Log2(1.0 / SnesUnityPitch);
            double maximum = rootMidiNote + SemitonesPerOctave * Math.Log2((double)MaximumSnesPitch / SnesUnityPitch);
            return Math.Clamp(midiNote, minimum, maximum);
        }

        private static void GetRange(ChipKind chip, ChannelKind channel, out double minimum, out double maximum)
        {
            minimum = GetFrequency(0);
            maximum = GetFrequency(127);
            if (chip == ChipKind.Nes)
            {
                double divider = channel == ChannelKind.Triangle ? 32 : 16;
                minimum = NesClock / (divider * (MaximumTimer + 1));
                maximum = NesClock / (divider * (MinimumPulseTimer + 1));
            }
            else if (chip == ChipKind.GameBoy)
            {
                double clock = channel == ChannelKind.Wave ? GameBoyWaveClock : GameBoyPulseClock;
                minimum = clock / (MaximumTimer + 1);
                maximum = clock;
            }
            else if (chip == ChipKind.Snes)
            {
                maximum = (double)SnesSampleRate / SnesWaveformLength * Math.Min(MaximumSnesRatio, (double)MaximumSnesPitch / SnesUnityPitch);
            }
        }
    }
}
