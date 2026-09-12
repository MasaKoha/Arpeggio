using System.Text.Json.Serialization;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>トーンから独立して発音するノイズ一声。</summary>
    public sealed record SfxNoiseParameters
    {
        /// <summary>ノイズを発音するか。</summary>
        [JsonPropertyOrder(0)]
        public bool Enabled { get; init; } = false;

        /// <summary>ノイズ専用の包絡。</summary>
        [JsonPropertyOrder(1)]
        public SfxEnvelopeParameters Envelope { get; init; } = new SfxEnvelopeParameters();
    }
}
