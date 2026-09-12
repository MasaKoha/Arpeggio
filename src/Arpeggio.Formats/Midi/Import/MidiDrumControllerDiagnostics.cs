using System;
using System.Collections.Generic;
using Arpeggio.Formats.Midi.Import.Tempo;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>元 Off に依存せず固定実時間 gate 内の音量変更を検出する。</summary>
    internal static class MidiDrumControllerDiagnostics
    {
        private const int VolumeController = 7;
        private const int ExpressionController = 11;
        private const int ResetControllers = 121;

        internal static void Diagnose(MidiFile file, IReadOnlyList<MidiNote> notes,
            MidiTempoMap tempoMap, ConversionReport report)
        {
            int volume = MidiChannelState.DefaultVolume;
            int expression = MidiChannelState.DefaultExpression;
            int noteIndex = 0;
            long maximumEnd = 0;
            foreach (MidiEvent current in file.Events)
            {
                if (current.Channel != MidiChannelState.DrumChannel || current.Kind != MidiMessageKind.ControlChange)
                {
                    continue;
                }
                while (noteIndex < notes.Count && Precedes(notes[noteIndex].Source, current))
                {
                    MidiNote note = notes[noteIndex++];
                    if (note.IsDrum)
                    {
                        maximumEnd = Math.Max(maximumEnd, MidiInstrumentMapper.GetDrumEnd(note, tempoMap));
                    }
                }
                bool changed = ApplyController(current, ref volume, ref expression);
                if (changed && maximumEnd > tempoMap.GetTimeNumerator(current.Tick))
                {
                    report.AddWarning(current.Diagnose("ControllerDuringNoteIgnored", "打楽器の固定 gate 内の音量変更は次の NoteOn から適用しました。"));
                }
            }
        }

        private static bool ApplyController(MidiEvent current, ref int volume, ref int expression)
        {
            int previousVolume = volume;
            int previousExpression = expression;
            switch (current.DataOne)
            {
                case VolumeController:
                    volume = current.DataTwo;
                    break;
                case ExpressionController:
                    expression = current.DataTwo;
                    break;
                case ResetControllers:
                    volume = MidiChannelState.DefaultVolume;
                    expression = MidiChannelState.DefaultExpression;
                    break;
            }
            return volume != previousVolume || expression != previousExpression;
        }

        private static bool Precedes(MidiEvent onset, MidiEvent change)
        {
            if (onset.Tick != change.Tick)
            {
                return onset.Tick < change.Tick;
            }
            if (onset.SourceTrack != change.SourceTrack)
            {
                return onset.SourceTrack < change.SourceTrack;
            }
            return onset.SourceEvent < change.SourceEvent;
        }
    }
}
