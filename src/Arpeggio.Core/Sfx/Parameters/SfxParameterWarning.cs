namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>入力を拒否しないパラメータ診断。</summary>
    public sealed record SfxParameterWarning
    {
        /// <summary>安定した警告コード。</summary>
        public string Code { get; init; } = string.Empty;

        /// <summary>parameters を基点とする正規パス。全体は空文字。</summary>
        public string ParameterPath { get; init; } = string.Empty;

        /// <summary>日本語の説明。</summary>
        public string Message { get; init; } = string.Empty;
    }
}
