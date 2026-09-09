using Arpeggio.Core.Document;

namespace Arpeggio.Core.Sfx
{
    /// <summary>再生成で全置換する現在の生成領域の件数。title と定義は含めない。</summary>
    public sealed class SfxReplacementSummary
    {
        internal SfxReplacementSummary(Song song)
        {
            InstrumentCount = song.Instruments.Count;
            TrackCount = song.Tracks.Count;
            foreach (Track track in song.Tracks)
            {
                NoteCount += track.Notes.Count;
            }
        }

        /// <summary>置換対象の音色数。未参照の音色も含む。</summary>
        public int InstrumentCount { get; }

        /// <summary>設定ごと置換するトラック数。空トラックも含む。</summary>
        public int TrackCount { get; }

        /// <summary>置換前の全ノート数。</summary>
        public int NoteCount { get; }
    }
}
