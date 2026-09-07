using System.Text.Json.Serialization;

namespace Arpeggio.Core.Document
{
    /// <summary>発音タイミングと音色参照。</summary>
    public sealed class Note
    {
        /// <summary>開始 tick。</summary>
        [JsonPropertyOrder(0)]
        public int Tick { get; set; }

        /// <summary>長さ tick。</summary>
        [JsonPropertyOrder(1)]
        public int DurationTicks { get; set; } = Song.FixedTicksPerBeat;

        /// <summary>MIDI ノート番号。</summary>
        [JsonPropertyOrder(2)]
        public int MidiNote { get; set; } = 60;

        /// <summary>音量（0〜15）。</summary>
        [JsonPropertyOrder(3)]
        public int Volume { get; set; } = 15;

        /// <summary>音色識別子。</summary>
        [JsonPropertyOrder(4)]
        public int InstrumentId { get; set; } = 1;

        /// <summary>ノート単位の効果。</summary>
        [JsonPropertyOrder(5)]
        public NoteEffect[] Effects { get; set; } = System.Array.Empty<NoteEffect>();
    }
}
