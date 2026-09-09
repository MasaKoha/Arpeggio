namespace Arpeggio.Core.Sfx
{
    /// <summary>トーンから独立して発音するノイズ一声。</summary>
    public sealed record SfxNoiseParameters
    {
        /// <summary>ノイズを発音するか。</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>ノイズ専用の包絡。</summary>
        public SfxEnvelopeParameters Envelope { get; init; } = new SfxEnvelopeParameters();
    }
}
