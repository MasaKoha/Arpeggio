using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Sfx
{
    /// <summary>一括検証を通過した部分 patch の候補値。</summary>
    public sealed record SfxParameterPatchResult
    {
        /// <summary>現在値へ適用し正規化した完全なパラメータ。</summary>
        public SfxParameters Parameters { get; init; } = new SfxParameters();

        /// <summary>正規化した変更前後の値が異なるか。</summary>
        public bool Changed { get; init; }

        /// <summary>候補値の検証警告。</summary>
        public IReadOnlyList<SfxParameterWarning> Warnings { get; init; } = Array.Empty<SfxParameterWarning>();
    }
}
