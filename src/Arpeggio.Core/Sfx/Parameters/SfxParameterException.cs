using System;

namespace Arpeggio.Core.Sfx.Parameters
{
    /// <summary>修正対象の正規パスと安定したコードを持つ SFX 入力エラー。</summary>
    public sealed class SfxParameterException : ArgumentException
    {
        /// <summary>コード・位置・説明と、任意の解析原因を保持する。</summary>
        public SfxParameterException(string code, string parameterPath, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            ParameterPath = parameterPath;
        }

        /// <summary>InvalidParameter または UnsupportedParameter。</summary>
        public string Code { get; }

        /// <summary>parameters を基点とする正規パス。ルートは空文字。</summary>
        public string ParameterPath { get; }

        internal static SfxParameterException Invalid(string path, string message)
        {
            return new SfxParameterException("InvalidParameter", path, message);
        }

        internal static SfxParameterException Unsupported(string path)
        {
            return new SfxParameterException("UnsupportedParameter", path, $"'{path}' は現在のチップで使用できない項目です。");
        }
    }
}
