using System;

namespace Arpeggio.Core.Document
{
    /// <summary>ソングの形式または制約違反を表す。</summary>
    public sealed class SongValidationException : Exception
    {
        /// <summary>修正対象の説明を保持する。</summary>
        public SongValidationException(string message) : base(message)
        {
        }

        /// <summary>形式エラーの原因と修正対象の説明を保持する。</summary>
        public SongValidationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
