using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Midi.Import.Voice
{
    /// <summary>量子化済み発音をチップの単声候補へ決定的に配置し、競合と音域制限を診断する。</summary>
    public sealed class MidiVoiceAllocator
    {
        private const int MaximumNoteVolume = 15;
        private readonly ConversionReport _report;
        private readonly MidiPolyphonyMode _polyphony;
        private readonly ChannelKind[] _channels;
        private readonly int[][] _candidates;
        private readonly List<MidiAllocatedNote>[] _tracks;
        private readonly MidiPitchRange?[] _pitchRanges;
        private long _droppedNotes;
        private long _truncatedNotes;
        private long _excludedNotes;

        private MidiVoiceAllocator(MidiImportOptions options, bool hasDrums, ConversionReport report)
        {
            _report = report;
            _polyphony = options.Polyphony;
            _channels = ChipLayout.GetChannels(options.Chip);
            _candidates = MidiChannelCandidates.Create(options, hasDrums, report);
            _tracks = new List<MidiAllocatedNote>[_channels.Length];
            _pitchRanges = new MidiPitchRange?[_channels.Length];
            for (int trackIndex = 0; trackIndex < _channels.Length; trackIndex++)
            {
                _tracks[trackIndex] = new List<MidiAllocatedNote>();
                ChannelKind channel = _channels[trackIndex];
                if (channel == ChannelKind.Pulse || channel == ChannelKind.Triangle || channel == ChannelKind.Wave)
                {
                    _pitchRanges[trackIndex] = MidiPitchRange.ForChip(options.Chip, channel);
                }
            }
        }

        /// <summary>全 ChipLayout トラックを不変列で返す。設定・先行エラー・全音消失時は null。音色生成や ID 採番は行わない。</summary>
        public static IReadOnlyList<MidiVoiceTrack>? Allocate(IReadOnlyList<MidiVoiceNote> notes, MidiImportOptions options, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(notes);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Midi || report.Chip != options.Chip)
            {
                throw new ArgumentException("出力チップが一致する MIDI レポートが必要です。", nameof(report));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            ValidateOptions(options, notes, report);
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var ordered = new List<MidiVoiceNote>(notes);
            bool hasDrums = ordered.Exists(note => note.Timing.Source.IsDrum);
            var allocator = new MidiVoiceAllocator(options, hasDrums, report);
            if (report.ErrorCount != 0)
            {
                return null;
            }
            ordered.Sort(CompareNotes);
            foreach (MidiVoiceNote note in ordered)
            {
                allocator.Assign(note);
            }
            return allocator.Complete();
        }

        private static void ValidateOptions(MidiImportOptions options, IReadOnlyList<MidiVoiceNote> notes, ConversionReport report)
        {
            if (options.Chip != ChipKind.Nes && options.Chip != ChipKind.GameBoy && options.Chip != ChipKind.Snes)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "MIDI の出力は NES / GB / SNES に対応しています。"));
            }
            if (options.Polyphony != MidiPolyphonyMode.StealOldest && options.Polyphony != MidiPolyphonyMode.DropNew)
            {
                report.AddError(new ConversionDiagnostic("InvalidOptions", "polyphony は steal-oldest または drop-new を指定してください。"));
            }
            if (notes.Count > ConversionLimits.MaximumMidiNoteOns)
            {
                report.AddError(new ConversionDiagnostic("SourceNoteLimitExceeded", "MIDI 発音数の上限を超えています。"));
                return;
            }
            if (options.Chip == ChipKind.Snes)
            {
                foreach (MidiVoiceNote note in notes)
                {
                    if (note.SampleRange is null)
                    {
                        report.AddError(note.Timing.Source.Source.Diagnose("InvalidMidiVoice", "SNES の割り当てには選択済みサンプルの音域が必要です。"));
                    }
                }
            }
        }

        private void Assign(MidiVoiceNote note)
        {
            int[] candidates = _candidates[note.Timing.Source.Source.Channel - 1];
            if (candidates.Length == 0)
            {
                _excludedNotes++;
                return;
            }
            int outputTrack = FindAvailable(candidates, note.Timing.StartTick);
            if (outputTrack < 0 && _polyphony == MidiPolyphonyMode.StealOldest)
            {
                outputTrack = FindOldest(candidates, note.Timing.StartTick);
                if (outputTrack >= 0)
                {
                    Truncate(outputTrack, note.Timing.StartTick);
                }
            }
            if (outputTrack < 0)
            {
                _droppedNotes++;
                _report.AddWarning(DescribeGate(note, "PolyphonyReduced", "空き声がないため新しい発音を破棄しました。", null, note.Timing.StartTick));
                return;
            }
            int pitch = ClampPitch(note, outputTrack);
            int volume = ConvertVolume(note, outputTrack);
            _tracks[outputTrack].Add(new MidiAllocatedNote(note, outputTrack, note.Timing.EndTick, pitch, volume));
        }

        private int FindAvailable(int[] candidates, int startTick)
        {
            foreach (int outputTrack in candidates)
            {
                List<MidiAllocatedNote> track = _tracks[outputTrack];
                if (track.Count == 0 || track[track.Count - 1].EndTick <= startTick)
                {
                    return outputTrack;
                }
            }
            return -1;
        }

        private int FindOldest(int[] candidates, int startTick)
        {
            int selected = -1;
            MidiAllocatedNote? oldest = null;
            foreach (int outputTrack in candidates)
            {
                List<MidiAllocatedNote> track = _tracks[outputTrack];
                MidiAllocatedNote current = track[track.Count - 1];
                // 同じ量子化 tick に採用した新音は、元の On 時刻が異なっていても保護する。
                if (current.StartTick == startTick)
                {
                    continue;
                }
                if (oldest is null || current.StartTick < oldest.StartTick ||
                    (current.StartTick == oldest.StartTick && current.Source.Timing.Source.EffectiveVolume < oldest.Source.Timing.Source.EffectiveVolume))
                {
                    selected = outputTrack;
                    oldest = current;
                }
            }
            return selected;
        }

        private void Truncate(int outputTrack, int endTick)
        {
            List<MidiAllocatedNote> track = _tracks[outputTrack];
            MidiAllocatedNote previous = track[track.Count - 1];
            track[track.Count - 1] = new MidiAllocatedNote(previous.Source, outputTrack, endTick, previous.Pitch, previous.Volume);
            _truncatedNotes++;
            _report.AddWarning(DescribeGate(previous.Source, "NoteTruncated", "古い発音を打ち切り、後で再開しません。", outputTrack, endTick));
        }

        private int ClampPitch(MidiVoiceNote note, int outputTrack)
        {
            if (_channels[outputTrack] == ChannelKind.Noise)
            {
                return note.Pitch;
            }
            MidiPitchRange range = _pitchRanges[outputTrack] ?? note.SampleRange!;
            int pitch = Math.Clamp(note.Pitch, range.Minimum, range.Maximum);
            if (pitch != note.Pitch)
            {
                _report.AddWarning(note.Timing.Source.Source.Diagnose("MidiPitchClamped", "割り当て先の整数音域へ最寄りクランプしました。") with
                {
                    OutputTrack = outputTrack, OutputTick = note.Timing.StartTick,
                    Original = note.Pitch.ToString(CultureInfo.InvariantCulture), Converted = pitch.ToString(CultureInfo.InvariantCulture)
                });
            }
            return pitch;
        }

        private int ConvertVolume(MidiVoiceNote note, int outputTrack)
        {
            int volume = note.Timing.Source.Volume;
            if (_channels[outputTrack] == ChannelKind.Triangle && volume != MaximumNoteVolume)
            {
                _report.AddWarning(note.Timing.Source.Source.Diagnose("TriangleVolumeIgnored", "Triangle の音量を 15 に固定しました。") with
                {
                    OutputTrack = outputTrack, OutputTick = note.Timing.StartTick,
                    Original = volume.ToString(CultureInfo.InvariantCulture), Converted = MaximumNoteVolume.ToString(CultureInfo.InvariantCulture)
                });
                return MaximumNoteVolume;
            }
            return volume;
        }

        private IReadOnlyList<MidiVoiceTrack>? Complete()
        {
            var tracks = new MidiVoiceTrack[_tracks.Length];
            var channelIndices = new Dictionary<ChannelKind, int>();
            long accepted = 0;
            for (int trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
            {
                ChannelKind channel = _channels[trackIndex];
                channelIndices.TryGetValue(channel, out int channelIndex);
                channelIndices[channel] = channelIndex + 1;
                tracks[trackIndex] = new MidiVoiceTrack(trackIndex, channel, channelIndex, _tracks[trackIndex]);
                accepted += _tracks[trackIndex].Count;
                RecordAssignments(trackIndex);
            }
            _report.SetStatistic("acceptedMidiNotes", accepted);
            _report.SetStatistic("polyphonyNotesDropped", _droppedNotes);
            _report.SetStatistic("truncatedMidiNotes", _truncatedNotes);
            _report.SetStatistic("explicitlyExcludedMidiNotes", _excludedNotes);
            if (accepted == 0)
            {
                _report.AddError(new ConversionDiagnostic("NoPlayableNotes", "変換後に発音可能なノートがありません。"));
                return null;
            }
            return Array.AsReadOnly(tracks);
        }

        private void RecordAssignments(int outputTrack)
        {
            var counts = new long[ConversionLimits.MaximumMidiChannel];
            foreach (MidiAllocatedNote note in _tracks[outputTrack])
            {
                counts[note.Source.Timing.Source.Source.Channel - 1]++;
            }
            for (int channelIndex = 0; channelIndex < counts.Length; channelIndex++)
            {
                if (counts[channelIndex] > 0)
                {
                    _report.SetStatistic(FormattableString.Invariant($"midiChannel.{channelIndex + 1}.outputTrack.{outputTrack}.notes"), counts[channelIndex]);
                }
            }
        }

        private static ConversionDiagnostic DescribeGate(MidiVoiceNote note, string code, string message, int? outputTrack, int endTick)
        {
            MidiNote source = note.Timing.Source;
            return source.Source.Diagnose(code, message) with
            {
                OutputTrack = outputTrack, OutputTick = note.Timing.StartTick,
                Original = FormattableString.Invariant($"midiTick={source.StartTick}, midiDuration={source.EndTick - source.StartTick}, outputTick={note.Timing.StartTick}, outputDuration={note.Timing.EndTick - note.Timing.StartTick}"),
                Converted = FormattableString.Invariant($"outputTick={note.Timing.StartTick}, outputDuration={endTick - note.Timing.StartTick}")
            };
        }

        internal static int CompareNotes(MidiVoiceNote left, MidiVoiceNote right)
        {
            int comparison = left.Timing.StartTick.CompareTo(right.Timing.StartTick);
            if (comparison != 0)
            {
                return comparison;
            }
            MidiNote leftSource = left.Timing.Source;
            MidiNote rightSource = right.Timing.Source;
            comparison = rightSource.IsDrum.CompareTo(leftSource.IsDrum);
            if (comparison == 0 && leftSource.IsDrum)
            {
                comparison = left.DrumPriority.CompareTo(right.DrumPriority);
            }
            if (comparison == 0)
            {
                comparison = rightSource.EffectiveVolume.CompareTo(leftSource.EffectiveVolume);
            }
            if (comparison == 0 && !leftSource.IsDrum)
            {
                comparison = rightSource.Pitch.CompareTo(leftSource.Pitch);
                if (comparison == 0)
                {
                    comparison = leftSource.Source.Channel.CompareTo(rightSource.Source.Channel);
                }
            }
            if (comparison == 0)
            {
                comparison = leftSource.Source.SourceTrack.CompareTo(rightSource.Source.SourceTrack);
            }
            return comparison != 0 ? comparison : leftSource.Source.SourceEvent.CompareTo(rightSource.Source.SourceEvent);
        }
    }
}
