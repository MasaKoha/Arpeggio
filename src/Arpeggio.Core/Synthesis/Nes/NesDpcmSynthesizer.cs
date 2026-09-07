using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Nes
{
    /// <summary>M1 では DPCM ノートを受け付けて無音を維持する。</summary>
    public sealed class NesDpcmSynthesizer : IChannelSynthesizer
    {
        /// <summary>ほかのチャンネルと同じ生成契約で無音チャンネルを作る。</summary>
        public NesDpcmSynthesizer(int sampleRate = 44100)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
        }
        /// <summary>M2 で実装するサンプル再生の予約入口。</summary>
        public void NoteOn(int midiNote, int volume, Instrument instrument, ReadOnlySpan<NoteEffect> effects) { }
        /// <summary>無音を維持する。</summary>
        public void NoteOff() { }
        /// <summary>加算契約に従い、既存バッファを変更しない。</summary>
        public void Render(Span<float> buffer) { }
        /// <summary>M1 では進行状態を持たない。</summary>
        public void AdvanceFrame() { }
    }
}
