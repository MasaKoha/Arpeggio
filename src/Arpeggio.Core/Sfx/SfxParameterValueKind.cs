namespace Arpeggio.Core.Sfx
{
    /// <summary>パラメータが受理する JSON 値の種類。</summary>
    public enum SfxParameterValueKind
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>真偽値。</summary>
        Boolean = 1,
        /// <summary>整数。</summary>
        Integer = 2,
        /// <summary>有限実数。</summary>
        Number = 3,
        /// <summary>正式名による文字列選択。</summary>
        Choice = 4
    }
}
