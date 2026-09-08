namespace Arpeggio.Formats.Midi
{
    /// <summary>採用先と打ち切り後の gate・音域制限後の音程を保持する不変の発音。</summary>
    public sealed class MidiAllocatedNote
    {
        internal MidiAllocatedNote(MidiVoiceNote source, int outputTrack, int endTick, int pitch, int volume)
        {
            Source = source;
            OutputTrack = outputTrack;
            EndTick = endTick;
            Pitch = pitch;
            Volume = volume;
        }

        /// <summary>音色採番と元位置診断へ引き継ぐ割り当て入力。</summary>
        public MidiVoiceNote Source { get; }
        /// <summary>ChipLayout の 0 始まり出力トラック番号。</summary>
        public int OutputTrack { get; }
        /// <summary>絶対開始 tick。</summary>
        public int StartTick => Source.Timing.StartTick;
        /// <summary>打ち切りを反映した絶対終了 tick。</summary>
        public int EndTick { get; }
        /// <summary>正の長さ。</summary>
        public int DurationTicks => EndTick - StartTick;
        /// <summary>割り当て先の整数音域に制限した音程。Noise は selection のまま。</summary>
        public int Pitch { get; }
        /// <summary>Triangle の 15 固定を反映した音量。</summary>
        public int Volume { get; }
    }
}
