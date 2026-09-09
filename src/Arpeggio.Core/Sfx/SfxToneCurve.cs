using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Sfx
{
    /// <summary>トーンの包絡・基準音程と、別々に加算するピッチ・ジャンプ列。</summary>
    public sealed class SfxToneCurve
    {
        internal SfxToneCurve(SfxEnvelopeCurve envelope, int anchorMidiNote, int repeatFrames,
            int pitchChangeFrames, Macro pitchMacro, Macro arpeggioMacro)
        {
            Envelope = envelope;
            AnchorMidiNote = anchorMidiNote;
            RepeatFrames = repeatFrames;
            PitchChangeFrames = pitchChangeFrames;
            PitchMacro = pitchMacro;
            ArpeggioMacro = arpeggioMacro;
        }

        /// <summary>repeat で戻さないトーン専用の包絡。</summary>
        public SfxEnvelopeCurve Envelope { get; }

        /// <summary>基準周波数に最も近い整数 MIDI 音程。</summary>
        public int AnchorMidiNote { get; }

        /// <summary>量子化後の反復周期。0は無効。</summary>
        public int RepeatFrames { get; }

        /// <summary>量子化後の音程ジャンプ待ちフレーム数。</summary>
        public int PitchChangeFrames { get; }

        /// <summary>基準音の端数・slide・delta・vibrato を含むセント列。結果が所有する。</summary>
        public Macro PitchMacro { get; }

        /// <summary>一回の音程ジャンプを反復周期ごとに戻す半音差列。結果が所有する。</summary>
        public Macro ArpeggioMacro { get; }
    }
}
