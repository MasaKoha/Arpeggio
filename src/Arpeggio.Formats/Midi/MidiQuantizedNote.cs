using System;

namespace Arpeggio.Formats.Midi
{
    /// <summary>元の発音情報と、正の長さを保証した量子化済み gate。</summary>
    public sealed class MidiQuantizedNote
    {
        /// <summary>声割り当て前の絶対開始・終了 tick を固定する。</summary>
        public MidiQuantizedNote(MidiNote source, int startTick, int endTick)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (startTick < 0 || endTick <= startTick)
            {
                throw new ArgumentOutOfRangeException(nameof(endTick), "発音は非負の開始位置と正の長さが必要です。");
            }
            Source = source;
            StartTick = startTick;
            EndTick = endTick;
        }

        /// <summary>On の出自・実効音量・元 gate。</summary>
        public MidiNote Source { get; }
        /// <summary>量子化後の絶対開始 tick。</summary>
        public int StartTick { get; }
        /// <summary>短音延長を含む、量子化後の絶対終了 tick。</summary>
        public int EndTick { get; }
    }
}
