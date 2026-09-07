using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Synthesis.GameBoy
{
    /// <summary>32 要素の 4 bit 波形メモリを再生する GB チャンネル。</summary>
    public sealed class GbWaveSynthesizer : ChannelSynthesizer
    {
        private const int WaveformLength = 32;
        private const int MaximumLevel = 15;
        private const int FullOutputPercent = 100;
        private int[] _waveform = System.Array.Empty<int>();
        private double _outputLevel;

        /// <summary>指定レートの波形メモリ合成器を作る。</summary>
        public GbWaveSynthesizer(int sampleRate = 44100) : base(sampleRate, ChipKind.GameBoy, ChannelKind.Wave) { }

        /// <summary>波形参照と段階音量を発音へ結び付ける。</summary>
        protected override void ConfigureInstrument(Instrument instrument)
        {
            var wave = (GbWaveInstrument)instrument;
            ConfigureMacros(null, wave.ArpeggioMacro, wave.PitchMacro);
            _waveform = wave.Waveform;
            _outputLevel = (double)wave.OutputLevel / FullOutputPercent;
        }

        /// <summary>波形メモリの現在位置を符号付き振幅へ変換する。</summary>
        protected override double ReadSample()
        {
            int index = (int)(Phase * WaveformLength);
            return (2.0 * _waveform[index] / MaximumLevel - 1) * Volume * _outputLevel;
        }
    }
}
