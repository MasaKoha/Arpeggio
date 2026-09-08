using System.Collections.Generic;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>生成側の型を使わずに読み戻した VGM のヘッダー・命令・タグ。</summary>
    internal sealed class ParsedVgm
    {
        internal uint Version { get; init; }
        internal long FileBytes { get; init; }
        internal int DataStart { get; init; }
        internal int Gd3Start { get; init; }
        internal uint HeaderSamples { get; init; }
        internal long WaitSamples { get; init; }
        internal uint NesClock { get; init; }
        internal uint GameBoyClock { get; init; }
        internal uint Gd3Version { get; init; }
        internal uint Gd3PayloadBytes { get; init; }
        internal int EndCommandPosition { get; init; }
        internal IReadOnlyList<int> Waits { get; init; } = new List<int>();
        internal IReadOnlyList<(long Sample, int Address, int Value, int Order)> Writes { get; init; } =
            new List<(long Sample, int Address, int Value, int Order)>();
        internal IReadOnlyList<string> Gd3Fields { get; init; } = new List<string>();
    }
}
