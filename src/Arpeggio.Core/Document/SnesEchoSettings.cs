using System.Text.Json.Serialization;

namespace Arpeggio.Core.Document
{
    /// <summary>ソング全体の SNES エコー設定。</summary>
    public sealed class SnesEchoSettings
    {
        /// <summary>遅延時間（0〜240 ms、16 ms 刻み）。</summary>
        [JsonPropertyOrder(0)]
        public int DelayMilliseconds { get; set; }

        /// <summary>フィードバック（絶対値 1 未満）。</summary>
        [JsonPropertyOrder(1)]
        public double Feedback { get; set; }

        /// <summary>出力音量（0〜1）。</summary>
        [JsonPropertyOrder(2)]
        public double Volume { get; set; }
    }
}
