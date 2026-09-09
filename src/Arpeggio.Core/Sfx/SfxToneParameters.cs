using System.Text.Json.Serialization;

namespace Arpeggio.Core.Sfx
{
    /// <summary>トーン一声の音程曲線と独立した包絡。</summary>
    public sealed record SfxToneParameters
    {
        /// <summary>トーンを発音するか。</summary>
        [JsonPropertyOrder(0)]
        public bool Enabled { get; init; } = true;

        /// <summary>基準周波数（20〜12000 Hz）。</summary>
        [JsonPropertyOrder(1)]
        public double BaseFrequencyHz { get; init; } = 440;

        /// <summary>音程の速度（-360〜360 半音/秒）。</summary>
        [JsonPropertyOrder(2)]
        public double SlideSemitonesPerSecond { get; init; } = 0;

        /// <summary>音程の加速度（-1440〜1440 半音/秒²）。</summary>
        [JsonPropertyOrder(3)]
        public double DeltaSlideSemitonesPerSecondSquared { get; init; } = 0;

        /// <summary>ビブラートの片振幅（0〜200 cent）。</summary>
        [JsonPropertyOrder(4)]
        public double VibratoDepthCents { get; init; } = 0;

        /// <summary>ビブラート周波数（0〜20 Hz）。0は無効。</summary>
        [JsonPropertyOrder(5)]
        public double VibratoSpeedHz { get; init; } = 6;

        /// <summary>一度の音程ジャンプ（-24〜24半音）。</summary>
        [JsonPropertyOrder(6)]
        public int PitchChangeSemitones { get; init; } = 0;

        /// <summary>音程ジャンプの待ち時間（0〜5秒）。</summary>
        [JsonPropertyOrder(7)]
        public double PitchChangeTimeSeconds { get; init; } = 0.05;

        /// <summary>曲線を戻す周期。0は無効、正値は1/60〜5秒。</summary>
        [JsonPropertyOrder(8)]
        public double RepeatPeriodSeconds { get; init; } = 0;

        /// <summary>トーン専用の包絡。</summary>
        [JsonPropertyOrder(9)]
        public SfxEnvelopeParameters Envelope { get; init; } = new SfxEnvelopeParameters();
    }
}
