using System;
using System.Collections.Generic;

namespace Arpeggio.Formats.Midi
{
    /// <summary>境界・資源を検証した SMF。イベントは絶対 tick・元トラック・元イベント順で不変。</summary>
    public sealed class MidiFile
    {
        internal MidiFile(int format, int trackCount, int ticksPerQuarterNote, long endTick,
            string? trackName, List<MidiEvent> events)
        {
            Format = format;
            TrackCount = trackCount;
            TicksPerQuarterNote = ticksPerQuarterNote;
            EndTick = endTick;
            TrackName = trackName;
            Events = Array.AsReadOnly(events.ToArray());
        }

        /// <summary>SMF format 0 または 1。</summary>
        public int Format { get; }
        /// <summary>MTrk 数。</summary>
        public int TrackCount { get; }
        /// <summary>四分音符あたりの MIDI tick 数。</summary>
        public int TicksPerQuarterNote { get; }
        /// <summary>全 MTrk の最遅 EOT tick。</summary>
        public long EndTick { get; }
        /// <summary>track 0 の最初の非空 Track Name。存在しなければ null。</summary>
        public string? TrackName { get; }
        /// <summary>後段に必要な channel message・Tempo・EOT の安定整列済み列。</summary>
        public IReadOnlyList<MidiEvent> Events { get; }
    }
}
