using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>GbNoise チャンネルの音色。</summary>
    public sealed class GbNoiseInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.GbNoise;

        /// <summary>LFSR のビット幅（7 または 15）。</summary>
        [JsonPropertyOrder(0)]
        public int LfsrWidth { get; set; } = 15;

        /// <summary>任意の音量マクロ。</summary>
        [JsonPropertyOrder(1)]
        public Macro? VolumeMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(2)]
        public Macro? PitchMacro { get; set; }
    }
}
