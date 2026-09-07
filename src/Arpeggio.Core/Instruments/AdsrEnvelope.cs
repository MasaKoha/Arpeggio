using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>秒単位の ADSR エンベロープ。</summary>
    public readonly record struct AdsrEnvelope
    {
        /// <summary>立ち上がり・減衰・保持・解放を指定する。</summary>
        public AdsrEnvelope(double attackSeconds, double decaySeconds, double sustainLevel, double releaseSeconds)
        {
            AttackSeconds = attackSeconds;
            DecaySeconds = decaySeconds;
            SustainLevel = sustainLevel;
            ReleaseSeconds = releaseSeconds;
        }
        /// <summary>立ち上がり時間。</summary>
        [JsonPropertyOrder(0)]
        public double AttackSeconds { get; init; }
        /// <summary>保持レベルまでの減衰時間。</summary>
        [JsonPropertyOrder(1)]
        public double DecaySeconds { get; init; }
        /// <summary>保持レベル（0〜1）。</summary>
        [JsonPropertyOrder(2)]
        public double SustainLevel { get; init; }
        /// <summary>ノートオフ後の解放時間。</summary>
        [JsonPropertyOrder(3)]
        public double ReleaseSeconds { get; init; }
    }
}
