using System.Text.Json.Serialization;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
namespace Arpeggio.Core.Document
{
    /// <summary>編集可能なソングの正本。</summary>
    public sealed class Song
    {
        /// <summary>対応するファイル形式。</summary>
        public const int CurrentVersion = 1;
        /// <summary>固定の四分音符解像度。</summary>
        public const int FixedTicksPerBeat = 48;

        /// <summary>ファイル形式のバージョン。</summary>
        [JsonPropertyOrder(0)]
        public int Version { get; set; } = CurrentVersion;

        /// <summary>曲名。</summary>
        [JsonPropertyOrder(1)]
        public string Title { get; set; } = string.Empty;

        /// <summary>チップ種別。</summary>
        [JsonPropertyOrder(2)]
        public ChipKind Chip { get; set; }

        /// <summary>毎分の拍数。</summary>
        [JsonPropertyOrder(3)]
        public int TempoBpm { get; set; } = 150;

        /// <summary>四分音符あたりの tick 数。</summary>
        [JsonPropertyOrder(4)]
        public int TicksPerBeat { get; set; } = FixedTicksPerBeat;

        /// <summary>ソングの長さ。</summary>
        [JsonPropertyOrder(5)]
        public int LengthTicks { get; set; } = 768;

        /// <summary>繰り返し開始 tick。</summary>
        [JsonPropertyOrder(6)]
        public int LoopStartTick { get; set; }

        /// <summary>音色一覧。</summary>
        [JsonPropertyOrder(7)]
        public List<Instrument> Instruments { get; set; } = new List<Instrument>();

        /// <summary>規定順序のトラック一覧。</summary>
        [JsonPropertyOrder(8)]
        public List<Track> Tracks { get; set; } = new List<Track>();

        /// <summary>SNES エコー設定。</summary>
        [JsonPropertyOrder(9)]
        public SnesEchoSettings SnesEcho { get; set; } = new SnesEchoSettings();
    }
}
