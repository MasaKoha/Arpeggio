using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>GbWave チャンネルの音色。</summary>
    public sealed class GbWaveInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.GbWave;

        /// <summary>32 要素の 4 bit 波形。</summary>
        [JsonPropertyOrder(0)]
        public int[] Waveform { get; set; } = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };

        /// <summary>出力百分率（0・25・50・100）。</summary>
        [JsonPropertyOrder(1)]
        public int OutputLevel { get; set; } = 100;

        /// <summary>任意のアルペジオマクロ。</summary>
        [JsonPropertyOrder(2)]
        public Macro? ArpeggioMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(3)]
        public Macro? PitchMacro { get; set; }
    }
}
