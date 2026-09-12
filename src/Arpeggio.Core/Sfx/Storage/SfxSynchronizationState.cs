using System;
using System.Collections.Generic;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Storage
{
    /// <summary>再生成せず判定した編集可否と、現在音または保存意図の区別。</summary>
    public sealed class SfxSynchronizationState
    {
        internal SfxSynchronizationState(SfxParameters? parameters, params SfxEditabilityReason[] reasons)
        {
            Reasons = Array.AsReadOnly(reasons);
            Reason = reasons.Length == 0 ? SfxEditabilityReason.None : reasons[0];
            Editable = Reason == SfxEditabilityReason.None;
            Parameters = Editable ? parameters : null;
            SavedParameters = Editable ? null : parameters;
        }

        /// <summary>保存パラメータを現在音の値として直接編集できるか。</summary>
        public bool Editable { get; }

        /// <summary>主理由。両指紋不一致では SavedParametersChanged を優先する。</summary>
        public SfxEditabilityReason Reason { get; }

        /// <summary>検出した全理由。両指紋が不一致なら両方を含む。</summary>
        public IReadOnlyList<SfxEditabilityReason> Reasons { get; }

        /// <summary>同期済みの現在パラメータ。編集不可では null。</summary>
        public SfxParameters? Parameters { get; }

        /// <summary>同期が失われた保存意図。現在音のパラメータとは扱わない。</summary>
        public SfxParameters? SavedParameters { get; }
    }
}
