namespace Arpeggio.Formats.Midi
{
    /// <summary>テンポの有効区間と、その先頭までの丸めていない実時間。</summary>
    internal readonly record struct MidiTempoSegment(long Tick, long TimeNumerator, int MicrosecondsPerQuarter);
}
