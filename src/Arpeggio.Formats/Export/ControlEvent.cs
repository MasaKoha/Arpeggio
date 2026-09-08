namespace Arpeggio.Formats.Export
{
    /// <summary>PCM を持たない、絶対時刻とマクロ適用後の発音制御値。</summary>
    public readonly record struct ControlEvent
    {
        internal ControlEvent(long positionSamples, int trackIndex, ControlEventKind kind, ControlNote? note,
            long frame, double midiNote, double volume, int duty)
        {
            PositionSamples = positionSamples;
            TrackIndex = trackIndex;
            Kind = kind;
            Note = note;
            Frame = frame;
            MidiNote = midiNote;
            Volume = volume;
            Duty = duty;
        }

        /// <summary>44100 Hz の絶対サンプル位置。</summary>
        public long PositionSamples { get; }
        /// <summary>ソング内のトラック番号。</summary>
        public int TrackIndex { get; }
        /// <summary>停止・発音・継続更新の区別。</summary>
        public ControlEventKind Kind { get; }
        /// <summary>元ノート。未発音トラックの終端停止では null。</summary>
        public ControlNote? Note { get; }
        /// <summary>今回の発音から経過した制御フレーム数。周回での再発音は 0 に戻す。</summary>
        public long Frame { get; }
        /// <summary>全マクロ・効果を合成した連続 MIDI 音程。周期の量子化前。</summary>
        public double MidiNote { get; }
        /// <summary>全マクロ・効果を合成した 0〜1 の音量。チップ固有音量の適用前。</summary>
        public double Volume { get; }
        /// <summary>マクロ適用後の DutyCycle 整数値。</summary>
        public int Duty { get; }
    }
}
