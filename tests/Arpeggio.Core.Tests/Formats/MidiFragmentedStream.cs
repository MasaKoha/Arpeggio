using System;
using System.IO;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>シーク不能・短い読み取り・途中 I/O 失敗を再現する入力専用 Stream。</summary>
    internal sealed class MidiFragmentedStream : Stream
    {
        private readonly MemoryStream _source;
        private readonly long _failAfterBytes;

        internal MidiFragmentedStream(byte[] bytes, long failAfterBytes = long.MaxValue)
        {
            _source = new MemoryStream(bytes);
            _failAfterBytes = failAfterBytes;
        }

        /// <summary>所有元が閉じるまでは読み取り可能。</summary>
        public override bool CanRead => _source.CanRead;
        /// <summary>シークしない入力。</summary>
        public override bool CanSeek => false;
        /// <summary>書き込みは不可。</summary>
        public override bool CanWrite => false;
        /// <summary>長さへの依存を検出する。</summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>位置への依存を検出する。</summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        /// <summary>最大 1 byte を読み、指定位置からは I/O 失敗にする。</summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_source.Position >= _failAfterBytes)
            {
                throw new IOException("入力の途中失敗。");
            }
            return _source.Read(buffer, offset, Math.Min(1, count));
        }
        /// <summary>入力には flush が不要。</summary>
        public override void Flush() { }
        /// <summary>シークは不可。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <summary>長さの変更は不可。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <summary>書き込みは不可。</summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _source.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
