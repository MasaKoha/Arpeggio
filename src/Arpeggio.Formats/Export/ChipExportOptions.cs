namespace Arpeggio.Formats.Export
{
    /// <summary>NSF / VGM の変換設定。音声サンプルレートと末尾余白は指定しない。</summary>
    public sealed class ChipExportOptions
    {
        /// <summary>出力形式。明示指定を必須とする。</summary>
        public ConversionFormat Format { get; init; }
        /// <summary>初回全曲を含めた有限再生回数。</summary>
        public int Loops { get; init; } = 1;
        /// <summary>著作者。未指定時は空。</summary>
        public string Author { get; init; } = string.Empty;
        /// <summary>NSF 専用の権利表記。VGM では空以外を拒否する。</summary>
        public string Copyright { get; init; } = string.Empty;
        /// <summary>変換警告が一件でもあれば保存を拒否する。</summary>
        public bool Strict { get; init; }
    }
}
