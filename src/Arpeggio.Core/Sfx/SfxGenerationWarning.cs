namespace Arpeggio.Core.Sfx
{
    /// <summary>要求値を変更せず、生成時の離散化や効かない設定を説明する診断。</summary>
    public sealed record SfxGenerationWarning
    {
        /// <summary>安定した診断コード。</summary>
        public string Code { get; init; } = string.Empty;

        /// <summary>parameters を基点とする正規パス。全体は空文字。</summary>
        public string ParameterPath { get; init; } = string.Empty;

        /// <summary>tone または noise。全体への診断は null。</summary>
        public string? Layer { get; init; }

        /// <summary>対象範囲の先頭フレーム。単一パラメータへの診断は null。</summary>
        public int? FromFrame { get; init; }

        /// <summary>対象範囲の末尾フレーム（含む）。単一パラメータへの診断は null。</summary>
        public int? ToFrame { get; init; }

        /// <summary>パスに対応する単位の要求値。数値で表せない場合は null。</summary>
        public double? Requested { get; init; }

        /// <summary>要求値と同じ単位の実効値。一意に表せない場合は null。</summary>
        public double? Actual { get; init; }

        /// <summary>日本語の診断説明。</summary>
        public string Message { get; init; } = string.Empty;
    }
}
