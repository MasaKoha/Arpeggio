using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>SnesSample チャンネルの音色。</summary>
    public sealed class SnesSampleInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.SnesSample;

        /// <summary>生成する波形種別。</summary>
        [JsonPropertyOrder(0)]
        public SnesWaveformKind Waveform { get; set; } = SnesWaveformKind.Sine;

        /// <summary>内蔵サンプルをループ再生するか。</summary>
        [JsonPropertyOrder(1)]
        public bool Loop { get; set; } = true;

        /// <summary>ADSR エンベロープ。</summary>
        [JsonPropertyOrder(2)]
        public AdsrEnvelope Envelope { get; set; } = new AdsrEnvelope(0, 0, 1, 0.05);

        /// <summary>エコー送り量（0〜1）。</summary>
        [JsonPropertyOrder(3)]
        public double EchoSend { get; set; }

        /// <summary>左右定位（-1〜1）。</summary>
        [JsonPropertyOrder(4)]
        public double Pan { get; set; }

        /// <summary>任意のアルペジオマクロ。</summary>
        [JsonPropertyOrder(5)]
        public Macro? ArpeggioMacro { get; set; }

        /// <summary>任意のピッチマクロ。</summary>
        [JsonPropertyOrder(6)]
        public Macro? PitchMacro { get; set; }
    }
}
