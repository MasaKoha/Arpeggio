using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;

namespace Arpeggio.Core.Sfx
{
    /// <summary>保存した作成意図と生成列の指紋。再生時には解釈しない。</summary>
    public sealed record SfxDefinitionData
    {
        /// <summary>対応するパラメータ構造の版。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>対応するマクロ・ノート展開規則の版。</summary>
        public const int CurrentGeneratorVersion = 1;

        /// <summary>パラメータの構造・単位の版。</summary>
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        /// <summary>生成規則の版。保存や読み込みでは更新しない。</summary>
        public int GeneratorVersion { get; init; } = CurrentGeneratorVersion;

        /// <summary>保存した全パラメータ。生成列との一致は別途判定する。</summary>
        public SfxParameters Parameters { get; init; } = new SfxParameters();

        /// <summary>版と正規化パラメータの SHA-256。</summary>
        public string ParametersHash { get; init; } = string.Empty;

        /// <summary>初期値の正式プリセット名。手動変更後も出自として保持する。</summary>
        public string? SourcePreset { get; init; }

        /// <summary>最後に成功した乱数操作の出自。未実施なら null。</summary>
        public SfxRandomization? LastRandomization { get; init; }

        /// <summary>title と sfx を除く生成領域の SHA-256。</summary>
        public string GeneratedHash { get; init; } = string.Empty;
    }
}
