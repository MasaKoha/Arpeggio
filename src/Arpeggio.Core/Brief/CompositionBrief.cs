using System.Text.Json.Serialization;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Brief
{
    /// <summary>Song とは独立した、AI へ渡す一曲分の作曲指示。</summary>
    public sealed record CompositionBrief
    {
        /// <summary>対応する保存形式の版。</summary>
        public const int CurrentVersion = 1;

        /// <summary>新規作成時に曲名を省略した場合の仮題。</summary>
        public const string DefaultTitle = "無題";

        /// <summary>保存形式の版。</summary>
        [JsonPropertyOrder(0)]
        public int Version => CurrentVersion;

        /// <summary>必須の曲名・仮題。</summary>
        [JsonPropertyOrder(1)]
        public string Title { get; init; } = string.Empty;

        /// <summary>対象チップ。null は AI に一任する。</summary>
        [JsonPropertyOrder(2)]
        public ChipKind? Chip { get; init; }

        /// <summary>目安テンポ。null は AI に一任する。</summary>
        [JsonPropertyOrder(3)]
        public int? TempoBpm { get; init; }

        /// <summary>雰囲気・ジャンル・キーワード。</summary>
        [JsonPropertyOrder(4)]
        public string? Mood { get; init; }

        /// <summary>イントロ・メイン等の構成メモ。</summary>
        [JsonPropertyOrder(5)]
        public string? Structure { get; init; }

        /// <summary>声ごとの役割・使い方。</summary>
        [JsonPropertyOrder(6)]
        public string? Instrumentation { get; init; }

        /// <summary>参考曲・スタイル。</summary>
        [JsonPropertyOrder(7)]
        public string? References { get; init; }

        /// <summary>ループ長や避けたい表現等の制約。</summary>
        [JsonPropertyOrder(8)]
        public string? Constraints { get; init; }

        /// <summary>その他の自由記述。</summary>
        [JsonPropertyOrder(9)]
        public string? Notes { get; init; }
    }
}
