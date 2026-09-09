namespace Arpeggio.Core.Sfx
{
    /// <summary>保存した SFX パラメータを直接編集できない理由。</summary>
    public enum SfxEditabilityReason
    {
        /// <summary>既知版で両指紋が一致している。</summary>
        None = 0,
        /// <summary>通常ソングまたは従来プリセットで、定義がない。</summary>
        MissingDefinition = 1,
        /// <summary>保存後に音色・ノートなどの生成領域が変わった。</summary>
        GeneratedContentChanged = 2,
        /// <summary>保存後にパラメータが変わった。</summary>
        SavedParametersChanged = 3,
        /// <summary>構造・生成規則・乱数規則のいずれかが未知版。</summary>
        UnsupportedSfxVersion = 4
    }
}
