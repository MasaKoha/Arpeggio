using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Midi
{
    /// <summary>音色 ID 採番前の単声トラック。空トラックも ChipLayout の規定順で保持する。</summary>
    public sealed class MidiVoiceTrack
    {
        internal MidiVoiceTrack(int outputTrack, ChannelKind channel, int channelIndex, List<MidiAllocatedNote> notes)
        {
            OutputTrack = outputTrack;
            Channel = channel;
            ChannelIndex = channelIndex;
            Notes = Array.AsReadOnly(notes.ToArray());
        }

        /// <summary>0 始まりの出力トラック番号。</summary>
        public int OutputTrack { get; }
        /// <summary>規定チャンネル種別。</summary>
        public ChannelKind Channel { get; }
        /// <summary>同種内のチャンネル番号。</summary>
        public int ChannelIndex { get; }
        /// <summary>正の長さ・開始昇順・非重複の採用ノート。</summary>
        public IReadOnlyList<MidiAllocatedNote> Notes { get; }
    }
}
