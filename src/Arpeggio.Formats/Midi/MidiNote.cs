using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Formats.Midi
{
    /// <summary>On 時点の状態と元 gate を保持する不変の発音。出力 tick への量子化・声割り当て前。</summary>
    public sealed class MidiNote
    {
        internal MidiNote(MidiPendingNote note, long endTick)
        {
            Source = note.Source;
            EndTick = endTick;
            KeyOffTick = note.KeyOffTick;
            SoundOffTick = note.SoundOffTick;
            Program = note.Program;
            ChannelVolume = note.ChannelVolume;
            Expression = note.Expression;
            EffectiveVolume = note.EffectiveVolume;
            Volume = note.Volume;
        }

        /// <summary>元の NoteOn。チャンネルは 1 始まり、元トラックとイベントは 0 始まり。</summary>
        public MidiEvent Source { get; }
        /// <summary>絶対 MIDI 開始 tick。</summary>
        public long StartTick => Source.Tick;
        /// <summary>sustain 適用後の旋律終端。打楽器は比較用の元 gate 終端または入力終端。</summary>
        public long EndTick { get; }
        /// <summary>対応したキー Off の tick。入力終端補完では null。</summary>
        public long? KeyOffTick { get; }
        /// <summary>この On より後で最初に受けた CC120 の tick。打楽器固定 gate の上限にも使う。</summary>
        public long? SoundOffTick { get; }
        /// <summary>元 MIDI pitch。打楽器も元番号のまま。</summary>
        public int Pitch => Source.DataOne;
        /// <summary>元の正の velocity。</summary>
        public int Velocity => Source.DataTwo;
        /// <summary>On 時点の 0 始まり program。</summary>
        public int Program { get; }
        /// <summary>On 時点の CC7。</summary>
        public int ChannelVolume { get; }
        /// <summary>On 時点の CC11。</summary>
        public int Expression { get; }
        /// <summary>4 bit 化前の正規化実効音量。声割り当ての優先順位に使う。</summary>
        public double EffectiveVolume { get; }
        /// <summary>正の積を最低 1 とした 1〜15 の音量。</summary>
        public int Volume { get; }
        /// <summary>GM 打楽器チャンネルか。</summary>
        public bool IsDrum => Source.Channel == MidiChannelState.DrumChannel;
    }
}
