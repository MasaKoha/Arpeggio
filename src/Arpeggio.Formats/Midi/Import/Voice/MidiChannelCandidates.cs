using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Midi.Import.Voice
{
    /// <summary>自動候補と明示 map を検証し、呼び出し元の配列から独立した昇順候補を作る。</summary>
    internal static class MidiChannelCandidates
    {
        private const int NoiseTrack = 3;
        private const int FirstSnesDrumTrack = 6;

        internal static int[][] Create(MidiImportOptions options, bool hasDrums, ConversionReport report)
        {
            ChannelKind[] channels = ChipLayout.GetChannels(options.Chip);
            var candidates = new int[ConversionLimits.MaximumMidiChannel][];
            for (int channelIndex = 0; channelIndex < candidates.Length; channelIndex++)
            {
                candidates[channelIndex] = CreateAutomatic(options.Chip, channels.Length, channelIndex + 1, hasDrums);
            }
            long excludedChannels = 0;
            if (options.ChannelMap is not null)
            {
                foreach (KeyValuePair<int, IReadOnlyList<int>> entry in options.ChannelMap)
                {
                    if (!ValidateEntry(entry, channels, report))
                    {
                        continue;
                    }
                    var selected = new int[entry.Value.Count];
                    for (int candidateIndex = 0; candidateIndex < selected.Length; candidateIndex++)
                    {
                        selected[candidateIndex] = entry.Value[candidateIndex];
                    }
                    Array.Sort(selected);
                    candidates[entry.Key - 1] = selected;
                    if (selected.Length == 0)
                    {
                        excludedChannels++;
                    }
                }
            }
            report.SetStatistic("explicitlyExcludedMidiChannels", excludedChannels);
            return candidates;
        }

        private static int[] CreateAutomatic(ChipKind chip, int trackCount, int channel, bool hasDrums)
        {
            if (chip != ChipKind.Snes)
            {
                return channel == MidiChannelState.DrumChannel ? new[] { NoiseTrack } : new[] { 0, 1, 2 };
            }
            int first = channel == MidiChannelState.DrumChannel ? FirstSnesDrumTrack : 0;
            int end = channel != MidiChannelState.DrumChannel && hasDrums ? FirstSnesDrumTrack : trackCount;
            var candidates = new int[end - first];
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                candidates[candidateIndex] = first + candidateIndex;
            }
            return candidates;
        }

        private static bool ValidateEntry(KeyValuePair<int, IReadOnlyList<int>> entry, ChannelKind[] channels, ConversionReport report)
        {
            bool validChannel = entry.Key >= 1 && entry.Key <= ConversionLimits.MaximumMidiChannel;
            if (!validChannel || entry.Value is null)
            {
                report.AddError(new ConversionDiagnostic("InvalidChannelMap", "map のキーは MIDI ch 1〜16、値は候補配列が必要です。"));
                return false;
            }
            var seen = new HashSet<int>();
            foreach (int outputTrack in entry.Value)
            {
                if (outputTrack < 0 || outputTrack >= channels.Length || !seen.Add(outputTrack) ||
                    !IsCompatible(entry.Key, channels[outputTrack]))
                {
                    report.AddError(new ConversionDiagnostic("InvalidChannelMap", "map に重複・範囲外・チャンネル非互換の候補があります。")
                    {
                        SourceChannel = entry.Key, OutputTrack = outputTrack
                    });
                    return false;
                }
            }
            return true;
        }

        private static bool IsCompatible(int channel, ChannelKind outputKind)
        {
            if (outputKind == ChannelKind.Dpcm)
            {
                return false;
            }
            if (outputKind == ChannelKind.Sample)
            {
                return true;
            }
            return (channel == MidiChannelState.DrumChannel) == (outputKind == ChannelKind.Noise);
        }
    }
}
