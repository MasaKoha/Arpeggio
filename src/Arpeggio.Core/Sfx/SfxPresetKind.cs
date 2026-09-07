namespace Arpeggio.Core.Sfx
{
    /// <summary>短いソングとして生成できる効果音。</summary>
    public enum SfxPresetKind
    {
        /// <summary>未指定。</summary>
        None = 0,
        /// <summary>上昇スライド。</summary>
        Jump = 1,
        /// <summary>コイン取得。</summary>
        Coin = 2,
        /// <summary>短い衝撃。</summary>
        Hit = 3,
        /// <summary>減衰する爆発。</summary>
        Explosion = 4,
        /// <summary>段階的な強化。</summary>
        PowerUp = 5,
        /// <summary>高速下降するレーザー。</summary>
        Laser = 6,
        /// <summary>短い単音。</summary>
        Blip = 7,
        /// <summary>選択を知らせる二音。</summary>
        Select = 8
    }
}
