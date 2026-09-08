using System.Collections.Generic;

namespace Arpeggio.Formats.Midi
{
    /// <summary>全 MTrk で共有する単一 MIDI チャンネルのキー・pedal・On 用設定。</summary>
    internal sealed class MidiChannelState
    {
        internal const int DrumChannel = 10;
        internal const int DefaultVolume = 100;
        internal const int DefaultExpression = 127;
        private const int PitchCount = 128;
        private readonly Queue<MidiPendingNote>?[] _keys = new Queue<MidiPendingNote>?[PitchCount];
        private readonly HashSet<MidiPendingNote> _sounding = new HashSet<MidiPendingNote>();
        private readonly List<MidiPendingNote> _sustained = new List<MidiPendingNote>();
        private readonly List<MidiPendingNote> _drumsSinceSoundOff = new List<MidiPendingNote>();

        internal int Program { get; set; }
        internal int Volume { get; set; } = DefaultVolume;
        internal int Expression { get; set; } = DefaultExpression;
        internal bool Sustain { get; private set; }

        internal void NoteOn(MidiPendingNote note)
        {
            int pitch = note.Source.DataOne;
            Queue<MidiPendingNote> queue = _keys[pitch] ??= new Queue<MidiPendingNote>();
            queue.Enqueue(note);
            _sounding.Add(note);
            if (note.Source.Channel == DrumChannel)
            {
                _drumsSinceSoundOff.Add(note);
            }
        }

        internal bool NoteOff(int pitch, long tick)
        {
            Queue<MidiPendingNote>? queue = _keys[pitch];
            if (queue is null || queue.Count == 0)
            {
                return false;
            }
            ReleaseKey(queue.Dequeue(), tick);
            return true;
        }

        internal void SetSustain(bool sustain, long tick)
        {
            Sustain = sustain;
            if (sustain)
            {
                return;
            }
            foreach (MidiPendingNote note in _sustained)
            {
                note.EndTick = tick;
                _sounding.Remove(note);
            }
            _sustained.Clear();
        }

        internal void AllNotesOff(long tick)
        {
            foreach (Queue<MidiPendingNote>? queue in _keys)
            {
                if (queue is null)
                {
                    continue;
                }
                while (queue.Count > 0)
                {
                    ReleaseKey(queue.Dequeue(), tick);
                }
            }
        }

        internal void AllSoundOff(long tick)
        {
            foreach (MidiPendingNote note in _sounding)
            {
                note.EndTick = tick;
                note.SoundOffTick = tick;
            }
            // 元 Off 後の打楽器も、後段で作る固定 gate はまだ鳴っている可能性がある。
            foreach (MidiPendingNote note in _drumsSinceSoundOff)
            {
                note.SoundOffTick = tick;
            }
            _drumsSinceSoundOff.Clear();
            _sounding.Clear();
            _sustained.Clear();
            foreach (Queue<MidiPendingNote>? queue in _keys)
            {
                if (queue is not null)
                {
                    queue.Clear();
                }
            }
        }

        private void ReleaseKey(MidiPendingNote note, long tick)
        {
            note.KeyOffTick = tick;
            if (Sustain && note.Source.Channel != DrumChannel)
            {
                _sustained.Add(note);
                return;
            }
            note.EndTick = tick;
            _sounding.Remove(note);
        }
    }
}
