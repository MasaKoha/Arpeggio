namespace Arpeggio.Formats.Midi
{
    /// <summary>元位置と解釈に必要な値だけを保持する不変の MIDI イベント。</summary>
    public readonly record struct MidiEvent
    {
        internal MidiEvent(long tick, int sourceTrack, long sourceEvent, MidiMessageKind kind,
            int channel = 0, int dataOne = 0, int dataTwo = 0)
        {
            Tick = tick;
            SourceTrack = sourceTrack;
            SourceEvent = sourceEvent;
            Kind = kind;
            Channel = channel;
            DataOne = dataOne;
            DataTwo = dataTwo;
        }

        /// <summary>絶対 MIDI tick。</summary>
        public long Tick { get; }
        /// <summary>0 始まりの MTrk 番号。</summary>
        public int SourceTrack { get; }
        /// <summary>スキップしたイベントも数えた、トラック内の 0 始まりの番号。</summary>
        public long SourceEvent { get; }
        /// <summary>メッセージの種類。</summary>
        public MidiMessageKind Kind { get; }
        /// <summary>channel message は 1〜16、meta は 0。</summary>
        public int Channel { get; }
        /// <summary>第一データ。Tempo の場合は四分音符のマイクロ秒数。</summary>
        public int DataOne { get; }
        /// <summary>第二データ。存在しない場合は 0。</summary>
        public int DataTwo { get; }

        internal ConversionDiagnostic Diagnose(string code, string message)
        {
            return new ConversionDiagnostic(code, message)
            {
                SourceTrack = SourceTrack, SourceEvent = SourceEvent, SourceTick = Tick,
                SourceChannel = Channel == 0 ? null : Channel
            };
        }
    }
}
