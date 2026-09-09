using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.GameBoy
{
    /// <summary>GB の矩形波とフレーム単位の音量エンベロープ。</summary>
    public sealed class GbPulseSynthesizer : ChannelSynthesizer
    {
        private const int MaximumVolume = 15;
        private int _envelopeVolume;
        private int _envelopeStepFrames;
        private int _envelopeDirection;
        private int _frame;

        /// <summary>指定レートの矩形波合成器を作る。</summary>
        public GbPulseSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.GameBoy, ChannelKind.Pulse) { }

        /// <summary>デューティとハードウェア音量の初期状態を設定する。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var pulse = (GbPulseInstrument)instrument;
            ConfigureMacros(pulse.VolumeMacro, pulse.ArpeggioMacro, pulse.PitchMacro, pulse.DutyMacro, (int)pulse.Duty);
            _envelopeVolume = pulse.InitialVolume;
            _envelopeStepFrames = pulse.EnvelopeStepFrames;
            _envelopeDirection = pulse.EnvelopeIncreasing ? 1 : -1;
            _frame = 0;
        }

        /// <summary>指定間隔で音量を一段進める。</summary>
        public override void AdvanceFrame()
        {
            base.AdvanceFrame();
            _frame++;
            if (_envelopeStepFrames > 0 && _frame % _envelopeStepFrames == 0)
            {
                _envelopeVolume = Math.Clamp(_envelopeVolume + _envelopeDirection, 0, MaximumVolume);
            }
        }

        /// <summary>マクロ音量とエンベロープを掛けた矩形波を返す。</summary>
        protected override double ReadSample() => (Phase < GetDutyRatio(Duty) ? 1 : -1) * Volume * _envelopeVolume / MaximumVolume;
    }
}
