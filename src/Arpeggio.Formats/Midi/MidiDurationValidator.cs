using System.Collections.Generic;

namespace Arpeggio.Formats.Midi
{
    /// <summary>量子化前の実時間上限を、PPQN 共通分母の整数分子で検証する。</summary>
    internal static class MidiDurationValidator
    {
        private const int DefaultMicrosecondsPerQuarter = 500000;
        private const long MicrosecondsPerSecond = 1000000;

        internal static void Validate(IReadOnlyList<MidiEvent> events, int ticksPerQuarterNote, ConversionReport report)
        {
            long previousTick = 0;
            long numerator = 0;
            int tempo = DefaultMicrosecondsPerQuarter;
            long maximumNumerator = ConversionLimits.MaximumDurationSeconds * MicrosecondsPerSecond * ticksPerQuarterNote;
            foreach (MidiEvent current in events)
            {
                numerator = checked(numerator + checked((current.Tick - previousTick) * tempo));
                if (numerator > maximumNumerator)
                {
                    throw new MidiReadException("DurationLimitExceeded", "MIDI の演奏時間が 1800 秒を超えています。", current);
                }
                previousTick = current.Tick;
                if (current.Kind == MidiMessageKind.Tempo)
                {
                    tempo = current.DataOne;
                }
            }
            report.SetOutputMetrics(numerator / (double)(MicrosecondsPerSecond * ticksPerQuarterNote), 0);
        }
    }
}
