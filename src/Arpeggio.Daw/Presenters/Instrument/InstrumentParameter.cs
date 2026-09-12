using System;

namespace Arpeggio.Daw.Presenters.Instrument
{
    /// <summary>音色パラメータの入力値と選択可能値。</summary>
    public sealed class InstrumentParameter
    {
        /// <summary>保存形式の項目名。</summary>
        public required string Key { get; init; }
        /// <summary>単位・範囲を含む表示名。</summary>
        public required string Label { get; init; }
        /// <summary>表示する入力値。</summary>
        public required string Value { get; init; }
        /// <summary>空なら自由入力、それ以外は選択肢。</summary>
        public string[] Choices { get; init; } = Array.Empty<string>();
    }
}
