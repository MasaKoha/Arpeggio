using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Arpeggio.Formats;

namespace Arpeggio.Mcp
{
    /// <summary>MCP の JSON 文字列から、重複キーを失わず MIDI チャンネル候補を読む。</summary>
    internal static class McpMidiChannelMap
    {
        internal static IReadOnlyDictionary<int, IReadOnlyList<int>>? Parse(string? json, ConversionReport report)
        {
            if (json is null) { return null; }
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return Parse(document.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                report.AddError(new ConversionDiagnostic("InvalidChannelMap", $"channelMap が不正です: {exception.Message}"));
                return null;
            }
        }

        private static IReadOnlyDictionary<int, IReadOnlyList<int>> Parse(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("MIDI チャンネルをキー、候補配列を値にしたオブジェクトを指定してください。");
            }
            var map = new Dictionary<int, IReadOnlyList<int>>();
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int channel) ||
                    channel < 1 || channel > ConversionLimits.MaximumMidiChannel)
                {
                    throw new FormatException("map のキーは MIDI チャンネル 1〜16 で指定してください。");
                }
                if (!map.TryAdd(channel, ParseCandidates(property.Value)))
                {
                    throw new FormatException($"MIDI チャンネル {channel} のキーが重複しています。");
                }
            }
            return map;
        }

        private static IReadOnlyList<int> ParseCandidates(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("map の値は出力トラック番号の配列で指定してください。");
            }
            var candidates = new List<int>();
            foreach (JsonElement candidate in value.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Number || !candidate.TryGetInt32(out int track))
                {
                    throw new FormatException("候補トラック番号は整数で指定してください。");
                }
                candidates.Add(track);
            }
            return candidates;
        }
    }
}
