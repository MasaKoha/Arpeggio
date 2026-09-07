using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis
{
    /// <summary>シーケンサーから制御する単一チャンネルの合成契約。</summary>
    public interface IChannelSynthesizer
    {
        /// <summary>位相とマクロを初期化して発音する。</summary>
        void NoteOn(int midiNote, int volume, Instrument instrument, ReadOnlySpan<NoteEffect> effects);
        /// <summary>発音を終了し、対応音色ではリリースへ移る。</summary>
        void NoteOff();
        /// <summary>モノラル結果を加算する。オーディオ処理中の確保は禁止。</summary>
        void Render(Span<float> buffer);
        /// <summary>60 Hz 境界でマクロとエンベロープを進める。</summary>
        void AdvanceFrame();
    }
}
