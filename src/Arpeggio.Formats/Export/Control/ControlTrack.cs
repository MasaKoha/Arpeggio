using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Export.Control
{
    /// <summary>書き出し中に変更されないトラック設定。</summary>
    public sealed class ControlTrack
    {
        internal ControlTrack(Track track)
        {
            Channel = track.Channel;
            ChannelIndex = track.ChannelIndex;
            Pan = track.Pan;
            Muted = track.Muted;
        }

        /// <summary>チャンネルの種類。</summary>
        public ChannelKind Channel { get; }
        /// <summary>同種内のチャンネル番号。</summary>
        public int ChannelIndex { get; }
        /// <summary>変換前の連続パン。</summary>
        public double Pan { get; }
        /// <summary>固定スナップショットでのミュート。</summary>
        public bool Muted { get; }
    }
}
