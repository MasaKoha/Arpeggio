using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Sfx
{
    /// <summary>SNES の内蔵周期波形と固定 DSP ノイズ速度。</summary>
    public sealed record SfxSnesParameters
    {
        /// <summary>トーンの周期波形。Noise と None は不許可。</summary>
        public SnesWaveformKind Waveform { get; init; } = SnesWaveformKind.Pulse;

        /// <summary>DSP ノイズ速度（1〜31）。</summary>
        public int NoiseRate { get; init; } = 24;
    }
}
