using System;

namespace Arpeggio.Core.Brief
{
    /// <summary>作曲指示書の安定したエラーコードと修正位置を保持する。</summary>
    public sealed class CompositionBriefException : ArgumentException
    {
        /// <summary>コード・位置・説明と任意の原因を保持する。</summary>
        public CompositionBriefException(string code, string parameterPath, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            ParameterPath = parameterPath;
        }

        /// <summary>InvalidParameter・InvalidBrief・DestinationExists のいずれか。</summary>
        public string Code { get; }

        /// <summary>camelCase のフィールド名、または document・path・arguments。</summary>
        public string ParameterPath { get; }
    }
}
