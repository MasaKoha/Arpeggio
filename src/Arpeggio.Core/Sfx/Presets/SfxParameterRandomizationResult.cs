using System;
using System.Collections.Generic;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Presets
{
    /// <summary>保存・履歴適用前の乱数操作候補。同値なら出自を更新しない。</summary>
    public sealed record SfxParameterRandomizationResult
    {
        /// <summary>正規化済みの完全な候補値。</summary>
        public SfxParameters Parameters { get; init; } = new SfxParameters();

        /// <summary>正規化した現在値と候補値が異なるか。</summary>
        public bool Changed { get; init; }

        /// <summary>変更成功したrandomizeの正式プリセット名。nullなら既存の出自を保持する。</summary>
        public string? SourcePreset { get; init; }

        /// <summary>変更成功時に保存する最後の乱数操作。nullなら既存の記録を保持する。</summary>
        public SfxRandomization? Randomization { get; init; }

        /// <summary>仕様カタログ順の変更一覧。元値へ戻った補正も補正前の値を残す。</summary>
        public IReadOnlyList<SfxParameterChange> Changes { get; init; } = Array.Empty<SfxParameterChange>();

        /// <summary>全有効レイヤーの音量ゼロなど、パラメータ検証の警告。</summary>
        public IReadOnlyList<SfxParameterWarning> Warnings { get; init; } = Array.Empty<SfxParameterWarning>();
    }
}
