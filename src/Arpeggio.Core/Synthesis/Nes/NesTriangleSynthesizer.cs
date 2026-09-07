using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Nes
{
    /// <summary>32 段のシーケンスを持つ音量固定の NES 三角波。</summary>
    public sealed class NesTriangleSynthesizer : ChannelSynthesizer
    {
        private const int SequenceLength = 32;
        private const int MaximumLevel = 15;

        /// <summary>指定レートの三角波合成器を作る。</summary>
        public NesTriangleSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.Nes, ChannelKind.Triangle) { }

        /// <summary>音程マクロを設定し、音量はハードウェアどおり無視する。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var triangle = (NesTriangleInstrument)instrument;
            ConfigureMacros(null, triangle.ArpeggioMacro, triangle.PitchMacro);
        }

        /// <summary>4 bit の階段状三角波を求める。</summary>
        protected override double ReadSample()
        {
            int step = (int)(Phase * SequenceLength);
            int level = step <= MaximumLevel ? MaximumLevel - step : step - (MaximumLevel + 1);
            return 2.0 * level / MaximumLevel - 1;
        }
    }
}
