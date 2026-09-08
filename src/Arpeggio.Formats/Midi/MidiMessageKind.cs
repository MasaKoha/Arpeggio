namespace Arpeggio.Formats.Midi
{
    /// <summary>取り込みの後段へ渡す MIDI メッセージの種類。</summary>
    public enum MidiMessageKind
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>キーの解放。</summary>
        NoteOff = 1,
        /// <summary>キーの押下。velocity 0 も元の種類を保持する。</summary>
        NoteOn = 2,
        /// <summary>キー別の圧力。</summary>
        PolyphonicPressure = 3,
        /// <summary>チャンネルのコントローラー。</summary>
        ControlChange = 4,
        /// <summary>音色番号の変更。</summary>
        ProgramChange = 5,
        /// <summary>チャンネル全体の圧力。</summary>
        ChannelPressure = 6,
        /// <summary>ピッチベンド。</summary>
        PitchBend = 7,
        /// <summary>四分音符のマイクロ秒数。</summary>
        Tempo = 8,
        /// <summary>MTrk の終端。</summary>
        EndOfTrack = 9
    }
}
