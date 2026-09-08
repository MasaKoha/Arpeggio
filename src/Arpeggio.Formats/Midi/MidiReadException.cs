using System;

namespace Arpeggio.Formats.Midi
{
    /// <summary>I/O 例外と区別し、壊れた入力を共通診断へ戻す解析内部の中断。</summary>
    internal sealed class MidiReadException : Exception
    {
        internal MidiReadException(string code, string message, MidiEvent? source = null) : base(message)
        {
            Code = code;
            EventSource = source;
        }

        internal string Code { get; }
        internal MidiEvent? EventSource { get; }
    }
}
