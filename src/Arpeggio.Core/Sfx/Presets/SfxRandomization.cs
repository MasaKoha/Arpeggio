using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>再現と履歴確認に使う最後の乱数操作。乱数生成自体は行わない。</summary>
    public sealed record SfxRandomization
    {
        /// <summary>対応する固定乱数アルゴリズムの版。</summary>
        public const int CurrentAlgorithmVersion = 1;

        /// <summary>成功した乱数操作。</summary>
        [JsonPropertyOrder(0)]
        public SfxRandomizationOperation Operation { get; init; }

        /// <summary>乱数とパラメータ変更規則の版。</summary>
        [JsonPropertyOrder(1)]
        public int AlgorithmVersion { get; init; } = CurrentAlgorithmVersion;

        /// <summary>操作開始時の符号なし32 bitシード。0も有効。</summary>
        [JsonPropertyOrder(2)]
        public uint Seed { get; init; }

        /// <summary>randomize の要求カテゴリ。mutate では null。</summary>
        [JsonPropertyOrder(3)]
        public string? Category { get; init; }

        /// <summary>mutate の強さ（0〜1）。randomize では null。</summary>
        [JsonPropertyOrder(4)]
        public double? Strength { get; init; }

        /// <summary>mutate で固定した正規パス。randomize では null。</summary>
        [JsonPropertyOrder(5)]
        public IReadOnlyList<string>? Locks { get; init; }

        /// <summary>mutate の変更前パラメータの指紋。randomize では null。</summary>
        [JsonPropertyOrder(6)]
        public string? BaseParametersHash { get; init; }
    }
}
