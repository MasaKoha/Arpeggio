using System;
using System.IO;

namespace Arpeggio.Formats.Midi
{
    /// <summary>シークに依存せず、入力総量と現在のチャンク境界を超える読み取りを防ぐ。</summary>
    internal sealed class MidiBinaryInput
    {
        private const int ByteBits = 8;
        private const int VariableLengthBits = 7;
        private const int ContinuationMask = 0x80;
        private const int DataMask = 0x7F;
        private const int MaximumVariableLengthBytes = 4;
        private const int SkipBufferBytes = 4096;
        private readonly Stream _stream;
        private readonly byte[] _skipBuffer = new byte[SkipBufferBytes];
        private long _chunkEnd = long.MaxValue;

        internal MidiBinaryInput(Stream stream)
        {
            _stream = stream;
        }

        internal long BytesRead { get; private set; }
        internal long Remaining => _chunkEnd - BytesRead;

        internal void BeginChunk(uint length)
        {
            if (length > ConversionLimits.MaximumMidiBytes - BytesRead)
            {
                throw new MidiReadException("MidiInputLimitExceeded", "宣言されたチャンクが MIDI 入力上限を超えています。");
            }
            _chunkEnd = BytesRead + length;
        }

        internal void EndChunk()
        {
            if (Remaining != 0)
            {
                throw new MidiReadException("InvalidMidi", "チャンクの宣言長と内容が一致しません。");
            }
            _chunkEnd = long.MaxValue;
        }

        internal int ReadOptionalByte()
        {
            int value = _stream.ReadByte();
            if (value < 0)
            {
                return value;
            }
            if (BytesRead == ConversionLimits.MaximumMidiBytes)
            {
                throw new MidiReadException("MidiInputLimitExceeded", "MIDI 入力の 32 MiB 上限を超えています。");
            }
            BytesRead++;
            return value;
        }

        internal int ReadByte()
        {
            Require(1);
            int value = ReadOptionalByte();
            if (value < 0)
            {
                throw new MidiReadException("InvalidMidi", "MIDI 入力が途中で切れています。");
            }
            return value;
        }

        internal int ReadDataByte()
        {
            int value = ReadByte();
            if (value > DataMask)
            {
                throw new MidiReadException("InvalidMidi", "channel message のデータ byte に MSB が立っています。");
            }
            return value;
        }

        internal int ReadUInt16() => (ReadByte() << ByteBits) | ReadByte();

        internal uint ReadUInt32() => ((uint)ReadUInt16() << (ByteBits * 2)) | (uint)ReadUInt16();

        internal int ReadVariableLength()
        {
            int value = 0;
            for (int byteIndex = 0; byteIndex < MaximumVariableLengthBytes; byteIndex++)
            {
                int current = ReadByte();
                value = (value << VariableLengthBits) | (current & DataMask);
                if ((current & ContinuationMask) == 0)
                {
                    return value;
                }
            }
            throw new MidiReadException("InvalidMidi", "VLQ は終端を含む 4 byte 以下でなければなりません。");
        }

        internal void Require(long count)
        {
            if (count > Remaining)
            {
                throw new MidiReadException("InvalidMidi", "イベントの宣言長がチャンク境界を超えています。");
            }
            if (count > ConversionLimits.MaximumMidiBytes - BytesRead)
            {
                throw new MidiReadException("MidiInputLimitExceeded", "MIDI 入力の 32 MiB 上限を超えています。");
            }
        }

        internal byte[] ReadBytes(int count)
        {
            Require(count);
            var bytes = new byte[count];
            ReadInto(bytes, count);
            return bytes;
        }

        internal void Skip(long count)
        {
            Require(count);
            while (count > 0)
            {
                int nextCount = (int)Math.Min(count, _skipBuffer.Length);
                ReadInto(_skipBuffer, nextCount);
                count -= nextCount;
            }
        }

        private void ReadInto(byte[] bytes, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int received = _stream.Read(bytes, offset, count - offset);
                if (received == 0)
                {
                    throw new MidiReadException("InvalidMidi", "MIDI payload が途中で切れています。");
                }
                offset += received;
                BytesRead += received;
            }
        }
    }
}
