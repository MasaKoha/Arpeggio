using System;
using System.Collections.Generic;

namespace Arpeggio.Formats.Midi
{
    /// <summary>安定整列済み MIDI からチャンネル共有の FIFO・sustain を評価し、元ノート区間を確定する。</summary>
    public sealed class MidiNoteCollector
    {
        private const int BankSelectMostSignificant = 0;
        private const int ModulationController = 1;
        private const int VolumeController = 7;
        private const int PanController = 10;
        private const int ExpressionController = 11;
        private const int BankSelectLeastSignificant = 32;
        private const int SustainController = 64;
        private const int AllSoundOffController = 120;
        private const int ResetControllers = 121;
        private const int AllNotesOffController = 123;
        private const int SustainThreshold = 64;
        private const int CenterPan = 64;
        private const int CenterPitchBend = 8192;
        private const int MidiDataBits = 7;
        private readonly ConversionReport _report;
        private readonly MidiChannelState[] _channels = new MidiChannelState[ConversionLimits.MaximumMidiChannel];
        private readonly List<MidiPendingNote> _notes = new List<MidiPendingNote>();
        private readonly List<MidiEvent> _volumeChanges = new List<MidiEvent>();

        private MidiNoteCollector(ConversionReport report)
        {
            _report = report;
            for (int channelIndex = 0; channelIndex < _channels.Length; channelIndex++)
            {
                _channels[channelIndex] = new MidiChannelState();
            }
        }

        /// <summary>On 順の不変ノート列を返し、同じ MIDI レポートへ診断を追記する。先行エラー時は null。strict 警告でも後続診断のため収集を完了する。</summary>
        public static IReadOnlyList<MidiNote>? Collect(MidiFile file, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Midi)
            {
                throw new ArgumentException("MIDI のレポートが必要です。", nameof(report));
            }
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var collector = new MidiNoteCollector(report);
            return collector.CollectNotes(file);
        }

        private IReadOnlyList<MidiNote> CollectNotes(MidiFile file)
        {
            foreach (MidiEvent current in file.Events)
            {
                if (current.Channel != 0)
                {
                    ApplyEvent(current, _channels[current.Channel - 1]);
                }
            }
            List<MidiNote> notes = CompleteNotes(file.EndTick);
            DiagnoseVolumeChanges(notes);
            _report.AddLimitation("MIDI 音量は NoteOn 時点の velocity・CC7・CC11 を 4 bit 化し、正の音量を最低 1 とします。");
            _report.SetStatistic("collectedMidiNotes", notes.Count);
            return Array.AsReadOnly(notes.ToArray());
        }

        private void ApplyEvent(MidiEvent current, MidiChannelState channel)
        {
            switch (current.Kind)
            {
                case MidiMessageKind.NoteOn when current.DataTwo > 0:
                    var note = new MidiPendingNote(current, channel);
                    _notes.Add(note);
                    channel.NoteOn(note);
                    break;
                case MidiMessageKind.NoteOn:
                case MidiMessageKind.NoteOff:
                    if (!channel.NoteOff(current.DataOne, current.Tick) && current.Channel != MidiChannelState.DrumChannel)
                    {
                        Warn(current, "UnmatchedNoteOff", "対応する NoteOn がない Off を無視しました。");
                    }
                    break;
                case MidiMessageKind.ProgramChange:
                    channel.Program = current.DataOne;
                    break;
                case MidiMessageKind.ControlChange:
                    ApplyController(current, channel);
                    break;
                case MidiMessageKind.PitchBend:
                    if (((current.DataTwo << MidiDataBits) | current.DataOne) != CenterPitchBend)
                    {
                        WarnExpression(current);
                    }
                    break;
                case MidiMessageKind.PolyphonicPressure:
                    if (current.DataTwo != 0)
                    {
                        WarnExpression(current);
                    }
                    break;
                case MidiMessageKind.ChannelPressure:
                    if (current.DataOne != 0)
                    {
                        WarnExpression(current);
                    }
                    break;
            }
        }

        private void ApplyController(MidiEvent current, MidiChannelState channel)
        {
            int value = current.DataTwo;
            switch (current.DataOne)
            {
                case VolumeController:
                    RecordVolumeChange(current, channel.Volume != value);
                    channel.Volume = value;
                    break;
                case ExpressionController:
                    RecordVolumeChange(current, channel.Expression != value);
                    channel.Expression = value;
                    break;
                case SustainController:
                    channel.SetSustain(value >= SustainThreshold, current.Tick);
                    break;
                case AllSoundOffController:
                    channel.AllSoundOff(current.Tick);
                    break;
                case AllNotesOffController:
                    channel.AllNotesOff(current.Tick);
                    break;
                case ResetControllers:
                    RecordVolumeChange(current, channel.Volume != MidiChannelState.DefaultVolume || channel.Expression != MidiChannelState.DefaultExpression);
                    channel.Volume = MidiChannelState.DefaultVolume;
                    channel.Expression = MidiChannelState.DefaultExpression;
                    channel.SetSustain(false, current.Tick);
                    break;
                case PanController:
                    if (value != CenterPan)
                    {
                        Warn(current, "MidiPanIgnored", "中央以外の MIDI pan を省略しました。出力 Pan は 0 を使用します。");
                    }
                    break;
                case BankSelectMostSignificant:
                case BankSelectLeastSignificant:
                    if (value != 0)
                    {
                        Warn(current, "MidiBankIgnored", "非零の Bank Select を省略し GM bank 0 を使用します。");
                    }
                    break;
                case ModulationController:
                    if (value != 0)
                    {
                        WarnExpression(current);
                    }
                    break;
                default:
                    WarnExpression(current);
                    break;
            }
        }

        private List<MidiNote> CompleteNotes(long endTick)
        {
            var notes = new List<MidiNote>(_notes.Count);
            long silentNotes = 0;
            long zeroLengthNotes = 0;
            foreach (MidiPendingNote pending in _notes)
            {
                bool isDrum = pending.Source.Channel == MidiChannelState.DrumChannel;
                if (pending.EndTick is null && !isDrum)
                {
                    Warn(pending.Source, "UnclosedNote", "未終了の NoteOn を曲の入力終端で閉じました。");
                }
                if (pending.Volume == 0)
                {
                    silentNotes++;
                    continue;
                }
                long noteEnd = pending.EndTick ?? endTick;
                bool zeroLength = isDrum ? pending.SoundOffTick == pending.Source.Tick : noteEnd == pending.Source.Tick;
                if (zeroLength)
                {
                    zeroLengthNotes++;
                    Warn(pending.Source, "ZeroLengthNoteDropped", "入力上で長さ 0 の発音を破棄しました。");
                    continue;
                }
                notes.Add(new MidiNote(pending, noteEnd));
            }
            _report.SetStatistic("zeroVolumeNotesDropped", silentNotes);
            _report.SetStatistic("zeroLengthNotesDropped", zeroLengthNotes);
            return notes;
        }

        private void RecordVolumeChange(MidiEvent current, bool changed)
        {
            if (changed)
            {
                _volumeChanges.Add(current);
            }
        }

        private void DiagnoseVolumeChanges(IReadOnlyList<MidiNote> notes)
        {
            // 完成区間で判定し、同 tick の Off や長さ 0 の音に誤警告しない。各列を一度ずつ走査する。
            var maximumEnds = new long[ConversionLimits.MaximumMidiChannel];
            int noteIndex = 0;
            foreach (MidiEvent change in _volumeChanges)
            {
                while (noteIndex < notes.Count && Precedes(notes[noteIndex].Source, change))
                {
                    MidiNote note = notes[noteIndex++];
                    if (!note.IsDrum)
                    {
                        int channelIndex = note.Source.Channel - 1;
                        maximumEnds[channelIndex] = Math.Max(maximumEnds[channelIndex], note.EndTick);
                    }
                }
                if (maximumEnds[change.Channel - 1] > change.Tick)
                {
                    Warn(change, "ControllerDuringNoteIgnored", "発音期間内の音量変更は次の NoteOn から適用しました。");
                }
            }
        }

        private static bool Precedes(MidiEvent onset, MidiEvent change)
        {
            if (onset.Tick != change.Tick)
            {
                return onset.Tick < change.Tick;
            }
            if (onset.SourceTrack != change.SourceTrack)
            {
                return onset.SourceTrack < change.SourceTrack;
            }
            return onset.SourceEvent < change.SourceEvent;
        }

        private void WarnExpression(MidiEvent current) => Warn(current, "MidiExpressionIgnored", "非対応の MIDI 表情メッセージを省略しました。");

        private void Warn(MidiEvent source, string code, string message) => _report.AddWarning(source.Diagnose(code, message));
    }
}
