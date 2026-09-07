using System.Text.Json.Serialization;

namespace Arpeggio.Core.Document
{
    /// <summary>ノートに適用する効果とそのパラメータ。</summary>
    public readonly record struct NoteEffect
    {
        /// <summary>効果を指定して初期化する。</summary>
        public NoteEffect(NoteEffectKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }
        /// <summary>効果の種類。</summary>
        [JsonPropertyOrder(0)]
        public NoteEffectKind Kind { get; init; }
        /// <summary>種類ごとの効果量。</summary>
        [JsonPropertyOrder(1)]
        public int Value { get; init; }
    }
}
