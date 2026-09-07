using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>バンク名と八トラックの音色順を対応付ける。</summary>
    public static class SnesBankLayout
    {
        private static readonly IReadOnlyList<string> Orchestral = Array.AsReadOnly(new[]
            { "strings", "brass", "flute", "choir", "bass", "kick", "snare", "hat" });
        private static readonly IReadOnlyList<string> Band = Array.AsReadOnly(new[]
            { "lead", "organ", "pluck", "bass", "piano", "kick", "snare", "hat" });
        private static readonly IReadOnlyList<string> Chip = Array.AsReadOnly(new[]
            { "lead", "lead", "bass", "organ", "bell", "kick", "snare", "hat" });

        /// <summary>省略時は従来動作。未知名や数値文字列を拒否する。</summary>
        public static SnesBankKind Parse(string? name)
        {
            return name switch
            {
                null => SnesBankKind.None,
                "orchestral" => SnesBankKind.Orchestral,
                "band" => SnesBankKind.Band,
                "chip" => SnesBankKind.Chip,
                _ => throw new ArgumentException("bank は orchestral / band / chip です。", nameof(name))
            };
        }

        /// <summary>編集できない音色順を返す。</summary>
        public static IReadOnlyList<string> Get(SnesBankKind bank)
        {
            return bank switch
            {
                SnesBankKind.None => Array.Empty<string>(),
                SnesBankKind.Orchestral => Orchestral,
                SnesBankKind.Band => Band,
                SnesBankKind.Chip => Chip,
                _ => throw new ArgumentOutOfRangeException(nameof(bank))
            };
        }
    }
}
