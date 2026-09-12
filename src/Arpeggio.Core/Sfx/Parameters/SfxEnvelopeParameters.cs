using System.Text.Json.Serialization;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>レイヤーのピーク音量と秒単位の ASDecay 包絡。</summary>
    public sealed record SfxEnvelopeParameters
    {
        /// <summary>ピーク音量（0〜15）。</summary>
        [JsonPropertyOrder(0)]
        public int Volume { get; init; } = 12;

        /// <summary>立ち上がり時間（0〜1秒）。</summary>
        [JsonPropertyOrder(1)]
        public double AttackSeconds { get; init; } = 0;

        /// <summary>保持時間（0〜2秒）。</summary>
        [JsonPropertyOrder(2)]
        public double SustainSeconds { get; init; } = 0.05;

        /// <summary>ゼロまでの減衰時間（1/60〜2秒）。</summary>
        [JsonPropertyOrder(3)]
        public double DecaySeconds { get; init; } = 0.15;

        /// <summary>保持冒頭の相対的な強調（0〜1）。</summary>
        [JsonPropertyOrder(4)]
        public double Punch { get; init; } = 0;
    }
}
