namespace Arpeggio.Core.Sfx
{
    /// <summary>乱数操作による値変更と、包絡の条件を満たすための補正。</summary>
    public sealed record SfxParameterChange
    {
        /// <summary>変更対象の正規パラメータパス。</summary>
        public string ParameterPath { get; init; } = string.Empty;

        /// <summary>操作前の正規化済みの値。</summary>
        public object PreviousValue { get; init; } = 0.0;

        /// <summary>補正後の正規化済みの値。randomizeのレイヤー有効性も含む。</summary>
        public object Value { get; init; } = 0.0;

        /// <summary>punch条件の補正を受けた場合の補正直前の抽選値。それ以外はnull。</summary>
        public double? CorrectedFrom { get; init; }
    }
}
