using System;

namespace Arpeggio.Core.Sfx
{
    /// <summary>定義の状態または revision による編集拒否を表す安定した操作エラー。</summary>
    public sealed class SfxEditException : InvalidOperationException
    {
        internal SfxEditException(string code, string message) : base(message)
        {
            Code = code;
        }

        /// <summary>編集不可理由または RevisionConflict。</summary>
        public string Code { get; }
    }
}
