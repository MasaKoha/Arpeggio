using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>従来のソング雛形と独立した、完全なパラメータ初期値のカタログ項目。</summary>
    public sealed record SfxParameterPresetDescription
    {
        /// <summary>用途の識別子。</summary>
        public SfxPresetKind Kind { get; init; }

        /// <summary>保存する正式名。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>一声の曲線としての音の特徴。</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>初期値の対象チップ。</summary>
        public ChipKind Chip { get; init; }

        /// <summary>無効レイヤーも含む正規化済みの完全初期値。</summary>
        public SfxParameters Parameters { get; init; } = new SfxParameters();
    }
}
