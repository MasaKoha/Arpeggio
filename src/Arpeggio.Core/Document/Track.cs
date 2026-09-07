using System.Text.Json.Serialization;
using System.Collections.Generic;
namespace Arpeggio.Core.Document
{
    /// <summary>単一チャンネルのノート列。</summary>
    public sealed class Track
    {
        /// <summary>チャンネル種別。</summary>
        [JsonPropertyOrder(0)]
        public ChannelKind Channel { get; set; }

        /// <summary>同種チャンネル内の番号。</summary>
        [JsonPropertyOrder(1)]
        public int ChannelIndex { get; set; }

        /// <summary>表示名。</summary>
        [JsonPropertyOrder(2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>ミュート状態。</summary>
        [JsonPropertyOrder(3)]
        public bool Muted { get; set; }

        /// <summary>左右定位（-1〜1）。</summary>
        [JsonPropertyOrder(4)]
        public double Pan { get; set; }

        /// <summary>音色 ID 省略時の割り当て。null は従来のチャンネル別選択。</summary>
        [JsonPropertyOrder(6)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DefaultInstrumentId { get; set; }

        /// <summary>開始 tick 昇順のノート列。編集時はリスト参照を差し替える。</summary>
        [JsonPropertyOrder(5)]
        public List<Note> Notes { get; set; } = new List<Note>();
    }
}
