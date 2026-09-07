using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>NesTriangle チャンネルの音色。</summary>
    public sealed class NesTriangleInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.NesTriangle;

        /// <summary>任意のアルペジオマクロ。</summary>
        [JsonPropertyOrder(0)]
        public Macro? ArpeggioMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(1)]
        public Macro? PitchMacro { get; set; }
    }
}
