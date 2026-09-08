using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sequencing;

namespace Arpeggio.Formats.Export
{
    /// <summary>一トラックの境界を周回順に列挙する。展開後の全境界配列は確保しない。</summary>
    internal static class ControlBoundaries
    {
        internal static IEnumerable<long> Enumerate(Song song, int trackIndex, int loops, TickClock clock, ConversionReport report)
        {
            Track track = song.Tracks[trackIndex];
            if (track.Muted)
            {
                yield break;
            }
            long loopLength = song.LengthTicks - song.LoopStartTick;
            for (int cycle = 0; cycle < loops; cycle++)
            {
                long offsetTicks = cycle * loopLength;
                long startTick = cycle == 0 ? 0 : offsetTicks + song.LoopStartTick;
                yield return clock.TickToSamples(startTick);
                foreach (long boundary in EnumerateNotes(track, trackIndex, offsetTicks, startTick, clock, report))
                {
                    yield return boundary;
                }
                yield return clock.TickToSamples(offsetTicks + song.LengthTicks);
            }
        }

        private static IEnumerable<long> EnumerateNotes(Track track, int trackIndex, long offsetTicks, long cycleStartTick,
            TickClock clock, ConversionReport report)
        {
            for (int noteIndex = 0; noteIndex < track.Notes.Count; noteIndex++)
            {
                Note note = track.Notes[noteIndex];
                long endTick = offsetTicks + note.Tick + note.DurationTicks;
                if (endTick <= cycleStartTick)
                {
                    continue;
                }
                long originalStartTick = Math.Max(cycleStartTick, offsetTicks + note.Tick);
                long soundingStartTick = Math.Max(cycleStartTick, offsetTicks + note.Tick + GetDelay(note));
                long startSamples = clock.TickToSamples(soundingStartTick);
                long endSamples = clock.TickToSamples(endTick);
                if (startSamples == endSamples)
                {
                    report.AddError(new ConversionDiagnostic("ControlEventCollision", "正の発音期間が 44100 Hz 上で 0 サンプルへ潰れます。")
                    {
                        SourceTrack = trackIndex,
                        SourceEvent = noteIndex,
                        SourceTick = note.Tick,
                        OutputTrack = trackIndex,
                        OutputTick = soundingStartTick
                    });
                }
                yield return clock.TickToSamples(originalStartTick);
                yield return startSamples;
                yield return endSamples;
            }
        }

        private static int GetDelay(Note note)
        {
            foreach (NoteEffect effect in note.Effects)
            {
                if (effect.Kind == NoteEffectKind.Delay)
                {
                    return effect.Value;
                }
            }
            return 0;
        }
    }
}
