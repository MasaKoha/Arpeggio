using System.Text.Json.Serialization;

namespace Arpeggio.Core.Instruments
{
    /// <summary>発音開始から 60 Hz で進める値列。</summary>
    public sealed class Macro
    {
        /// <summary>フレームごとの値。</summary>
        [JsonPropertyOrder(0)]
        public int[] Values { get; set; } = System.Array.Empty<int>();

        /// <summary>ループ開始位置。-1 は末尾を保持。</summary>
        [JsonPropertyOrder(1)]
        public int LoopIndex { get; set; } = -1;
    }
}
