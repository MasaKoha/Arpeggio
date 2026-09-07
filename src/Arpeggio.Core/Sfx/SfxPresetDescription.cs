namespace Arpeggio.Core.Sfx
{
    /// <summary>コマンド名・説明・ソング本体長のカタログ項目。</summary>
    public readonly record struct SfxPresetDescription
    {
        /// <summary>プリセットの識別子。</summary>
        public SfxPresetKind Kind { get; init; }
        /// <summary>CLI / MCP 共通の小文字名。</summary>
        public string Name { get; init; }
        /// <summary>音の特徴と想定時間。</summary>
        public string Description { get; init; }
        /// <summary>テンポ 150 での最小ソング長。</summary>
        public int LengthTicks { get; init; }
    }
}
