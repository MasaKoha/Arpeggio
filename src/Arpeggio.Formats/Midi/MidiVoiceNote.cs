using System;

namespace Arpeggio.Formats.Midi
{
    /// <summary>声割り当てへ渡す確定 gate・変換音程・選択済みサンプル音域。音色自体は生成しない。</summary>
    public sealed class MidiVoiceNote
    {
        /// <summary>旋律の音程、Noise selection または SNES ドラム root と、必要な分類・音域を固定する。</summary>
        public MidiVoiceNote(MidiQuantizedNote timing, int pitch, MidiPitchRange? sampleRange = null,
            MidiDrumPriority drumPriority = MidiDrumPriority.None)
        {
            const int MaximumMidiPitch = 127;
            ArgumentNullException.ThrowIfNull(timing);
            if (pitch < 0 || pitch > MaximumMidiPitch)
            {
                throw new ArgumentOutOfRangeException(nameof(pitch));
            }
            if (drumPriority < MidiDrumPriority.None || drumPriority > MidiDrumPriority.Hat ||
                timing.Source.IsDrum != (drumPriority != MidiDrumPriority.None))
            {
                throw new ArgumentException("ch 10 の発音には確定した打楽器分類が必要です。", nameof(drumPriority));
            }
            Timing = timing;
            Pitch = pitch;
            SampleRange = sampleRange;
            DrumPriority = drumPriority;
        }

        /// <summary>元の発音情報と量子化済みの絶対 gate。</summary>
        public MidiQuantizedNote Timing { get; }
        /// <summary>音域制限前の出力音程または Noise selection。</summary>
        public int Pitch { get; }
        /// <summary>選択した SNES サンプルの整数音域。NES / GB では不要。</summary>
        public MidiPitchRange? SampleRange { get; }
        /// <summary>分類済み打撃の優先順位。旋律は None。</summary>
        public MidiDrumPriority DrumPriority { get; }
    }
}
