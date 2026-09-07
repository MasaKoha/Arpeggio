using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.Nes
{
    /// <summary>16 種の周期と長短の帰還位置を持つ NES ノイズ。</summary>
    public sealed class NesNoiseSynthesizer : ChannelSynthesizer
    {
        private const int MaximumMidiNote = 127;
        private const double ClockRate = 1789773;
        private const int RegisterWidth = 15;
        private const int ShortTap = 6;
        private const int LongTap = 1;
        private readonly int[] _periods = new int[] { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };
        private readonly NoiseOscillator _oscillator = new NoiseOscillator();
        private int _tap;
        private double _increment;

        /// <summary>指定レートのノイズ合成器を作る。</summary>
        public NesNoiseSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.Nes, ChannelKind.Noise) { }

        /// <summary>モードとマクロを設定し LFSR を既知状態へ戻す。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var noise = (NesNoiseInstrument)instrument;
            ConfigureMacros(noise.VolumeMacro, null, noise.PitchMacro);
            _tap = noise.NoiseMode == NoiseMode.Short ? ShortTap : LongTap;
            _oscillator.Reset();
            RefreshNoiseRate();
        }

        /// <summary>ピッチマクロからノイズ周期を更新する。</summary>
        public override void AdvanceFrame()
        {
            base.AdvanceFrame();
            RefreshNoiseRate();
        }

        /// <summary>LFSR の極性に音量を掛ける。</summary>
        protected override double ReadSample() => _oscillator.Read(_increment, RegisterWidth, _tap) * Volume;

        private void RefreshNoiseRate()
        {
            int selection = (int)Math.Round(Math.Clamp(MidiNote, 0, MaximumMidiNote));
            int periodIndex = selection % _periods.Length;
            _increment = ClockRate / _periods[periodIndex] / SampleRate;
        }
    }
}
