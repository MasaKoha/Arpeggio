using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>NesDpcm チャンネルの音色。</summary>
    public sealed class NesDpcmInstrument : Instrument
    {
        /// <summary>音色の種類。</summary>
        [JsonIgnore]
        public override InstrumentKind Kind => InstrumentKind.NesDpcm;


    }
}
