using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>チップ固有音色の共通識別情報。</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(NesPulseInstrument), "NesPulse")]
    [JsonDerivedType(typeof(NesTriangleInstrument), "NesTriangle")]
    [JsonDerivedType(typeof(NesNoiseInstrument), "NesNoise")]
    [JsonDerivedType(typeof(NesDpcmInstrument), "NesDpcm")]
    [JsonDerivedType(typeof(GbPulseInstrument), "GbPulse")]
    [JsonDerivedType(typeof(GbWaveInstrument), "GbWave")]
    [JsonDerivedType(typeof(GbNoiseInstrument), "GbNoise")]
    [JsonDerivedType(typeof(SnesSampleInstrument), "SnesSample")]
    public abstract class Instrument
    {
        /// <summary>ソング内で一意の識別子。</summary>
        [JsonPropertyOrder(-2)]
        public int Id { get; set; } = 1;
        /// <summary>表示名。</summary>
        [JsonPropertyOrder(-1)]
        public string Name { get; set; } = string.Empty;
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public abstract InstrumentKind Kind { get; }
    }
}
