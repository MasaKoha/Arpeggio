using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Nes
{
    /// <summary>NES の四種類のデューティを持つ矩形波チャンネル。</summary>
    public sealed class NesPulseSynthesizer : ChannelSynthesizer
    {
        /// <summary>指定レートの矩形波合成器を作る。</summary>
        public NesPulseSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.Nes, ChannelKind.Pulse) { }

        /// <summary>音色マクロを発音状態へ結び付ける。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var pulse = (NesPulseInstrument)instrument;
            ConfigureMacros(pulse.VolumeMacro, pulse.ArpeggioMacro, pulse.PitchMacro, pulse.DutyMacro, (int)pulse.Duty);
        }

        /// <summary>現在のデューティで矩形波を求める。</summary>
        protected override double ReadSample() => (Phase < GetDutyRatio(Duty) ? 1 : -1) * Volume;
    }
}
