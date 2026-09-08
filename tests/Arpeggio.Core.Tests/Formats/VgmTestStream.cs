using System;
using System.IO;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>Seek 不可と指定位置での部分 I/O 失敗を再現する、自己所有のメモリ付きテスト Stream。</summary>
    internal sealed class VgmTestStream : Stream
    {
        private readonly MemoryStream _contents = new MemoryStream();
        private readonly long _failureAfterBytes;
        private bool _disposed;

        internal VgmTestStream(long failureAfterBytes = long.MaxValue)
        {
            _failureAfterBytes = failureAfterBytes;
        }

        internal byte[] GetWrittenBytes() => _contents.ToArray();

        /// <summary>読み取りは提供しない。</summary>
        public override bool CanRead => false;
        /// <summary>ヘッダーの後戻り修正を検出するため Seek は提供しない。</summary>
        public override bool CanSeek => false;
        /// <summary>writer が呼び出し元所有の Stream を閉じていないか観測する。</summary>
        public override bool CanWrite => !_disposed;
        /// <summary>出力先の長さを要求する実装を検出する。</summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>絶対位置を要求する実装を検出する。</summary>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>メモリへの書き込みは即時反映される。</summary>
        public override void Flush() { }
        /// <summary>この出力先からは読み取れない。</summary>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>この出力先は後戻りできない。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <summary>部分出力を巻き戻す操作は提供しない。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>従来の Stream 書き込みを同じ故障境界へ渡す。</summary>
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        /// <summary>指定位置までは保存し、途中書き込み後に I/O 例外を送出する。</summary>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int writableBytes = (int)Math.Min(buffer.Length, _failureAfterBytes - _contents.Length);
            _contents.Write(buffer.Slice(0, writableBytes));
            if (writableBytes < buffer.Length)
            {
                throw new IOException("テスト用の途中書き込み失敗。");
            }
        }

        /// <summary>テスト自身が所有するメモリを解放する。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _contents.Dispose();
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
