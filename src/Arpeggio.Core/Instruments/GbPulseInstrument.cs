using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>GbPulse チャンネルの音色。</summary>
    public sealed class GbPulseInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.GbPulse;

        /// <summary>デューティ比。</summary>
        [JsonPropertyOrder(0)]
        public DutyCycle Duty { get; set; } = DutyCycle.Percent50;

        /// <summary>ハードウェアエンベロープの初期音量。</summary>
        [JsonPropertyOrder(1)]
        public int InitialVolume { get; set; } = 15;

        /// <summary>エンベロープが増加方向か。</summary>
        [JsonPropertyOrder(2)]
        public bool EnvelopeIncreasing { get; set; }

        /// <summary>音量変化間隔。0 は無効。</summary>
        [JsonPropertyOrder(3)]
        public int EnvelopeStepFrames { get; set; }

        /// <summary>任意の音量マクロ。</summary>
        [JsonPropertyOrder(4)]
        public Macro? VolumeMacro { get; set; }

        /// <summary>任意のアルペジオマクロ。</summary>
        [JsonPropertyOrder(5)]
        public Macro? ArpeggioMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(6)]
        public Macro? PitchMacro { get; set; }
    }
}
