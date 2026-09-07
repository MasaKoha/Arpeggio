using System;
using System.Globalization;

namespace Arpeggio.Core.Document
{
    /// <summary>音名と MIDI 番号を副作用なしで相互変換する。</summary>
    public static class NoteName
    {
        private const int SemitonesPerOctave = 12;
        private const int MaximumMidiNote = 127;
        private const int MidiOctaveOffset = 1;

        /// <summary>C5・C#5・Db5 または MIDI 番号を解釈する。MIDI 60 は C4。</summary>
        public static int Parse(string text)
        {
            string value = text.Trim();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int midiNote))
            {
                ValidateRange(midiNote);
                return midiNote;
            }
            if (value.Length < 2)
            {
                throw InvalidName(text);
            }
            int pitchClass = char.ToUpperInvariant(value[0]) switch
            {
                'C' => 0,
                'D' => 2,
                'E' => 4,
                'F' => 5,
                'G' => 7,
                'A' => 9,
                'B' => 11,
                _ => throw InvalidName(text)
            };
            int octaveStart = 1;
            if (value[octaveStart] == '#' || value[octaveStart] == 'b')
            {
                pitchClass += value[octaveStart] == '#' ? 1 : -1;
                octaveStart++;
            }
            if (!int.TryParse(value.Substring(octaveStart), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int octave))
            {
                throw InvalidName(text);
            }
            long parsedNote = ((long)octave + MidiOctaveOffset) * SemitonesPerOctave + pitchClass;
            if (parsedNote < 0 || parsedNote > MaximumMidiNote)
            {
                throw new ArgumentOutOfRangeException(nameof(text), text, "音名は MIDI 0〜127 の範囲で指定してください。");
            }
            return (int)parsedNote;
        }

        /// <summary>MIDI 番号をシャープ表記の音名へ変換する。</summary>
        public static string Format(int midiNote)
        {
            ValidateRange(midiNote);
            string pitchName = (midiNote % SemitonesPerOctave) switch
            {
                0 => "C",
                1 => "C#",
                2 => "D",
                3 => "D#",
                4 => "E",
                5 => "F",
                6 => "F#",
                7 => "G",
                8 => "G#",
                9 => "A",
                10 => "A#",
                11 => "B",
                _ => throw new InvalidOperationException("音階の計算結果が不正です。")
            };
            int octave = midiNote / SemitonesPerOctave - MidiOctaveOffset;
            return pitchName + octave.ToString(CultureInfo.InvariantCulture);
        }

        private static void ValidateRange(int midiNote)
        {
            if (midiNote < 0 || midiNote > MaximumMidiNote)
            {
                throw new ArgumentOutOfRangeException(nameof(midiNote), midiNote, "MIDI ノート番号は 0〜127 です。");
            }
        }

        private static ArgumentException InvalidName(string text)
        {
            return new ArgumentException($"音名 '{text}' が不正です。C5・C#5・Db5 または MIDI 0〜127 を指定してください。", nameof(text));
        }
    }
}
