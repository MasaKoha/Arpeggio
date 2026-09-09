namespace Arpeggio.Core.Sfx
{
    /// <summary>レイヤーのピーク音量と秒単位の ASDecay 包絡。</summary>
    public sealed record SfxEnvelopeParameters
    {
        /// <summary>ピーク音量（0〜15）。</summary>
        public int Volume { get; init; } = 12;

        /// <summary>立ち上がり時間（0〜1秒）。</summary>
        public double AttackSeconds { get; init; } = 0;

        /// <summary>保持時間（0〜2秒）。</summary>
        public double SustainSeconds { get; init; } = 0.05;

        /// <summary>ゼロまでの減衰時間（1/60〜2秒）。</summary>
        public double DecaySeconds { get; init; } = 0.15;

        /// <summary>保持冒頭の相対的な強調（0〜1）。</summary>
        public double Punch { get; init; } = 0;
    }
}
