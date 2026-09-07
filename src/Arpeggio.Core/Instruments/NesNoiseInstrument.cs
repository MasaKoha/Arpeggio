using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>NesNoise チャンネルの音色。</summary>
    public sealed class NesNoiseInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.NesNoise;

        /// <summary>LFSR の周期モード。</summary>
        [JsonPropertyOrder(0)]
        public NoiseMode NoiseMode { get; set; } = NoiseMode.Long;

        /// <summary>任意の音量マクロ。</summary>
        [JsonPropertyOrder(1)]
        public Macro? VolumeMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(2)]
        public Macro? PitchMacro { get; set; }
    }
}
