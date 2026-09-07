using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>NesPulse チャンネルの音色。</summary>
    public sealed class NesPulseInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.NesPulse;

        /// <summary>デューティ比。</summary>
        [JsonPropertyOrder(0)]
        public DutyCycle Duty { get; set; } = DutyCycle.Percent50;

        /// <summary>任意の音量マクロ。</summary>
        [JsonPropertyOrder(1)]
        public Macro? VolumeMacro { get; set; }

        /// <summary>任意のアルペジオマクロ。</summary>
        [JsonPropertyOrder(2)]
        public Macro? ArpeggioMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(3)]
        public Macro? PitchMacro { get; set; }

        /// <summary>任意のデューティマクロ。</summary>
        [JsonPropertyOrder(4)]
        public Macro? DutyMacro { get; set; }
    }
}
