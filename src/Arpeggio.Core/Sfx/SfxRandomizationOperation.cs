namespace Arpeggio.Core.Sfx
{
    /// <summary>保存する乱数操作の種類。</summary>
    public enum SfxRandomizationOperation
    {
        /// <summary>未指定。出自オブジェクトの操作としては不正。</summary>
        None = 0,
        /// <summary>カテゴリ初期値からの生成。</summary>
        Randomize = 1,
        /// <summary>現在パラメータからの変異。</summary>
        Mutate = 2
    }
}
