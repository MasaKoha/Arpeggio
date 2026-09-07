using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.GameBoy
{
    /// <summary>7 bit または 15 bit の帰還レジスタを持つ GB ノイズ。</summary>
    public sealed class GbNoiseSynthesizer : ChannelSynthesizer
    {
        private const int MaximumMidiNote = 127;
        private const int FeedbackTap = 1;
        private const double NoiseClock = 524288;
        private readonly NoiseOscillator _oscillator = new NoiseOscillator();
        private int _width;
        private double _increment;

        /// <summary>指定レートのノイズ合成器を作る。</summary>
        public GbNoiseSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.GameBoy, ChannelKind.Noise) { }

        /// <summary>LFSR の幅と音量・ピッチマクロを設定する。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var noise = (GbNoiseInstrument)instrument;
            ConfigureMacros(noise.VolumeMacro, null, noise.PitchMacro);
            _width = noise.LfsrWidth;
            _oscillator.Reset();
            RefreshNoiseRate();
        }

        /// <summary>ピッチマクロからノイズのクロック分周を更新する。</summary>
        public override void AdvanceFrame()
        {
            base.AdvanceFrame();
            RefreshNoiseRate();
        }

        /// <summary>LFSR の極性に音量を掛ける。</summary>
        protected override double ReadSample() => _oscillator.Read(_increment, _width, FeedbackTap) * Volume;

        private void RefreshNoiseRate()
        {
            const int OctaveMask = 7;
            const int NotesPerDivisorGroup = 8;
            int selection = (int)Math.Round(Math.Clamp(MidiNote, 0, MaximumMidiNote));
            double divisor = (selection & OctaveMask) + 1;
            int shift = (MaximumMidiNote - selection) / NotesPerDivisorGroup;
            _increment = NoiseClock / divisor / Math.Pow(2, shift + 1) / SampleRate;
        }
    }
}
