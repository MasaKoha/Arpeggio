namespace Arpeggio.Formats
{
    /// <summary>元位置と変換結果を特定できる不変の診断明細。</summary>
    public sealed record ConversionDiagnostic
    {
        /// <summary>診断コードと日本語の説明を指定する。</summary>
        public ConversionDiagnostic(string code, string message)
        {
            Code = code;
            Message = message;
        }

        /// <summary>機械判定用の安定した診断コード。</summary>
        public string Code { get; init; }
        /// <summary>診断内容。</summary>
        public string Message { get; init; }
        /// <summary>元の Song または SMF のトラック番号。</summary>
        public int? SourceTrack { get; init; }
        /// <summary>元 MIDI チャンネル。ユーザー表記の 1〜16。</summary>
        public int? SourceChannel { get; init; }
        /// <summary>元トラック内のイベント番号。</summary>
        public long? SourceEvent { get; init; }
        /// <summary>元ノートまたはイベントの tick。</summary>
        public long? SourceTick { get; init; }
        /// <summary>出力トラック番号。</summary>
        public int? OutputTrack { get; init; }
        /// <summary>出力ノートまたはイベントの tick。</summary>
        public long? OutputTick { get; init; }
        /// <summary>変換前の値の文字列表現。</summary>
        public string? Original { get; init; }
        /// <summary>変換後の値の文字列表現。</summary>
        public string? Converted { get; init; }
        /// <summary>この明細に集約された発生数。</summary>
        public long OccurrenceCount { get; init; } = 1;
        /// <summary>この原因の最大誤差。単位は診断コードの契約に従う。</summary>
        public double? MaximumError { get; init; }
    }
}
