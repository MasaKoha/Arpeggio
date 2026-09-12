using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Synthesis;

namespace Arpeggio.Formats.Midi.Import.Voice
{
    /// <summary>割り当て先で発音できる整数 MIDI 音域。SNES は選択済みサンプルの元レートも含む。</summary>
    public sealed class MidiPitchRange
    {
        private const int MinimumMidiPitch = 0;
        private const int MaximumMidiPitch = 127;
        private const int SemitonesPerOctave = 12;
        private const int SnesSampleRate = 32000;

        /// <summary>MIDI 0〜127 内の非空の整数音域を固定する。</summary>
        public MidiPitchRange(int minimum, int maximum)
        {
            if (minimum < MinimumMidiPitch || maximum > MaximumMidiPitch || minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum), "整数音域は MIDI 0〜127 内の非空区間が必要です。");
            }
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>発音可能な最小整数 MIDI 音。</summary>
        public int Minimum { get; }
        /// <summary>発音可能な最大整数 MIDI 音。</summary>
        public int Maximum { get; }

        /// <summary>NES / GB の公開 PitchTable から連続範囲の内側の整数端点を求める。Noise は対象外。</summary>
        public static MidiPitchRange ForChip(ChipKind chip, ChannelKind channel)
        {
            bool supported = (chip == ChipKind.Nes && (channel == ChannelKind.Pulse || channel == ChannelKind.Triangle)) ||
                (chip == ChipKind.GameBoy && (channel == ChannelKind.Pulse || channel == ChannelKind.Wave));
            if (!supported)
            {
                throw new ArgumentException("NES / GB の旋律チャンネルを指定してください。", nameof(channel));
            }
            double minimum = Math.Clamp(PitchTable.ClampMidiNote(chip, channel, MinimumMidiPitch), MinimumMidiPitch, MaximumMidiPitch);
            double maximum = Math.Clamp(PitchTable.ClampMidiNote(chip, channel, MaximumMidiPitch), MinimumMidiPitch, MaximumMidiPitch);
            return new MidiPitchRange((int)Math.Ceiling(minimum), (int)Math.Floor(maximum));
        }

        /// <summary>元レートと root を含む未クランプの SNES pitch が 1〜16383 になる整数音域を求める。</summary>
        public static MidiPitchRange ForSnesSample(int sampleRate, int rootMidiNote)
        {
            if (sampleRate <= 0 || rootMidiNote < MinimumMidiPitch || rootMidiNote > MaximumMidiPitch)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "正のサンプルレートと MIDI 0〜127 の root が必要です。");
            }
            int minimum = MaximumMidiPitch + 1;
            int maximum = MinimumMidiPitch - 1;
            for (int pitch = MinimumMidiPitch; pitch <= MaximumMidiPitch; pitch++)
            {
                double ratio = Math.Pow(2, (pitch - rootMidiNote) / (double)SemitonesPerOctave);
                double register = Math.Round(ratio * sampleRate / SnesSampleRate * PitchTable.SnesUnityPitch, MidpointRounding.AwayFromZero);
                if (register >= 1 && register <= PitchTable.MaximumSnesPitch)
                {
                    minimum = Math.Min(minimum, pitch);
                    maximum = pitch;
                }
            }
            return new MidiPitchRange(minimum, maximum);
        }
    }
}
