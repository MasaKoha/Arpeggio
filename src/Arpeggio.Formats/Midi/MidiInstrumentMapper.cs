using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;

namespace Arpeggio.Formats.Midi
{
    /// <summary>GM 音色・打楽器を分類し、確定 gate と採用済み音色を既存モデルへ写す。</summary>
    public static class MidiInstrumentMapper
    {
        private const int MaximumProgram = 127;
        private const int DrumRoot = 60;

        /// <summary>全収集音の音色分類・固定 gate・音域を確定し、量子化した割り当て入力を返す。</summary>
        public static IReadOnlyList<MidiVoiceNote>? Map(MidiFile file, IReadOnlyList<MidiNote> notes,
            MidiTempoMap tempoMap, MidiTickQuantizer quantizer, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(notes);
            ArgumentNullException.ThrowIfNull(tempoMap);
            ArgumentNullException.ThrowIfNull(quantizer);
            ValidateReport(report);
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var result = new List<MidiVoiceNote>(notes.Count);
            foreach (MidiNote note in notes)
            {
                MidiVoiceNote? mapped = MapNote(note, tempoMap, quantizer, report);
                if (mapped != null)
                {
                    result.Add(mapped);
                }
            }
            MidiDrumControllerDiagnostics.Diagnose(file, notes, tempoMap, report);
            return report.ErrorCount == 0 ? result.AsReadOnly() : null;
        }

        /// <summary>GM の全 128 program に対応する既存 SNES プリセット名を返す。</summary>
        public static string GetSnesPreset(int program)
        {
            if (program < 0 || program > MaximumProgram)
            {
                throw new ArgumentOutOfRangeException(nameof(program));
            }
            // 境界は設計書の 0 始まり GM family 表をそのまま表す。
            return program switch
            {
                <= 7 => "piano", <= 15 => "bell", <= 23 => "organ", <= 31 => "pluck",
                <= 39 => "bass", <= 51 => "strings", <= 55 => "choir", <= 63 => "brass",
                <= 71 => "lead", <= 79 => "flute", <= 87 => "lead", <= 95 => "strings",
                <= 103 => "bell", <= 111 => "pluck", <= 119 => "bell", _ => "lead"
            };
        }

        /// <summary>採用ノートを割り当て順に再走査し、未使用音色を作らず ID 1 から共有音色を生成する。</summary>
        public static MidiInstrumentMap? CreateInstruments(IReadOnlyList<MidiVoiceTrack> tracks, ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(tracks);
            ValidateReport(report);
            if (report.ErrorCount != 0)
            {
                return null;
            }
            var notes = tracks.SelectMany(track => track.Notes).ToList();
            notes.Sort((left, right) => MidiVoiceAllocator.CompareNotes(left.Source, right.Source));
            var instruments = new List<Instrument>();
            var shared = new Dictionary<string, Instrument>(StringComparer.Ordinal);
            var identifiers = new Dictionary<MidiAllocatedNote, int>();
            var programs = new HashSet<(int Channel, int Program)>();
            foreach (MidiAllocatedNote note in notes)
            {
                ChannelKind channel = tracks[note.OutputTrack].Channel;
                string key = GetInstrumentKey(note, channel, report.Chip);
                if (!shared.TryGetValue(key, out Instrument? instrument))
                {
                    instrument = CreateInstrument(note, channel, report.Chip);
                    instrument.Id = instruments.Count + 1;
                    instrument.Name = key;
                    shared.Add(key, instrument);
                    instruments.Add(instrument);
                    report.SetStatistic(FormattableString.Invariant($"instrument.{instrument.Id}.{key}"), 1);
                }
                identifiers.Add(note, instrument.Id);
                DiagnoseProgram(note, instrument, programs, report);
            }
            report.SetStatistic("generatedInstruments", instruments.Count);
            return new MidiInstrumentMap(instruments, identifiers);
        }

        internal static long GetDrumEnd(MidiNote note, MidiTempoMap tempoMap)
        {
            long fixedEnd = checked(tempoMap.GetTimeNumerator(note.StartTick) +
                MidiDrumDefinition.Get(note.Pitch).GateMicroseconds * tempoMap.TicksPerQuarterNote);
            return note.SoundOffTick is long stop ? Math.Min(fixedEnd, tempoMap.GetTimeNumerator(stop)) : fixedEnd;
        }

        private static MidiVoiceNote? MapNote(MidiNote note, MidiTempoMap tempoMap,
            MidiTickQuantizer quantizer, ConversionReport report)
        {
            if (!note.IsDrum)
            {
                MidiQuantizedNote? timing = quantizer.QuantizeNote(note);
                return timing is null ? null : new MidiVoiceNote(timing, note.Pitch,
                    GetSampleRange(GetSnesPreset(note.Program), report.Chip));
            }
            MidiDrumDefinition drum = MidiDrumDefinition.Get(note.Pitch);
            long end = GetDrumEnd(note, tempoMap);
            if (note.KeyOffTick is not long keyOff || tempoMap.GetTimeNumerator(keyOff) != end)
            {
                report.AddWarning(note.Source.Diagnose("DrumGateReplaced", "打楽器の元 gate を固定実時間 gate と CC120 上限で置き換えました。") with
                {
                    Original = note.KeyOffTick?.ToString(CultureInfo.InvariantCulture) ?? "no note off",
                    Converted = FormattableString.Invariant($"endMicroseconds={end / (decimal)tempoMap.TicksPerQuarterNote}")
                });
            }
            if (drum.IsFallback)
            {
                report.AddWarning(note.Source.Diagnose("UnknownDrumMapped", "表にない打楽器を snare へ写しました。") with
                {
                    Original = note.Pitch.ToString(CultureInfo.InvariantCulture), Converted = drum.Preset
                });
            }
            int pitch = DrumRoot;
            if (report.Chip != ChipKind.Snes)
            {
                pitch = report.Chip == ChipKind.Nes ? drum.NesSelection : drum.GameBoySelection;
                report.AddWarning(note.Source.Diagnose("DrumApproximated", "打楽器を分類別の Noise 音色へ近似しました。") with
                {
                    Original = note.Pitch.ToString(CultureInfo.InvariantCulture), Converted = pitch.ToString(CultureInfo.InvariantCulture)
                });
            }
            MidiQuantizedNote? drumTiming = quantizer.QuantizeNote(note, end);
            return drumTiming is null ? null : new MidiVoiceNote(drumTiming, pitch,
                GetSampleRange(drum.Preset, report.Chip), drum.Priority);
        }

        private static MidiPitchRange? GetSampleRange(string presetName, ChipKind chip)
        {
            if (chip != ChipKind.Snes)
            {
                return null;
            }
            SnesInstrumentPreset preset = SnesInstrumentCatalog.Get(presetName);
            return MidiPitchRange.ForSnesSample(preset.SampleRate, preset.RootMidiNote);
        }

        private static string GetInstrumentKey(MidiAllocatedNote note, ChannelKind channel, ChipKind chip)
        {
            MidiNote source = note.Source.Timing.Source;
            if (source.IsDrum)
            {
                return MidiDrumDefinition.Get(source.Pitch).Preset;
            }
            return chip == ChipKind.Snes ? GetSnesPreset(source.Program) : channel.ToString();
        }

        private static Instrument CreateInstrument(MidiAllocatedNote note, ChannelKind channel, ChipKind chip)
        {
            MidiNote source = note.Source.Timing.Source;
            if (chip == ChipKind.Snes)
            {
                string presetName = GetInstrumentKey(note, channel, chip);
                SnesInstrumentPreset preset = SnesInstrumentCatalog.Get(presetName);
                return new SnesSampleInstrument
                {
                    Preset = presetName, SampleRate = preset.SampleRate,
                    RootMidiNote = source.IsDrum ? DrumRoot : preset.RootMidiNote,
                    Loop = preset.Loop, LoopStart = preset.LoopStart, LoopEnd = preset.LoopEnd,
                    AdsrRegisters = preset.AdsrRegisters, Pan = 0, EchoSend = 0,
                    NoiseEnabled = false, PitchModulation = false
                };
            }
            if (source.IsDrum)
            {
                Macro volume = MidiDrumDefinition.Get(source.Pitch).CreateVolumeMacro();
                return chip == ChipKind.Nes
                    ? new NesNoiseInstrument { NoiseMode = NoiseMode.Long, VolumeMacro = volume }
                    : new GbNoiseInstrument { VolumeMacro = volume };
            }
            return (chip, channel) switch
            {
                (ChipKind.Nes, ChannelKind.Pulse) => new NesPulseInstrument(),
                (ChipKind.Nes, ChannelKind.Triangle) => new NesTriangleInstrument(),
                (ChipKind.GameBoy, ChannelKind.Pulse) => new GbPulseInstrument(),
                (ChipKind.GameBoy, ChannelKind.Wave) => new GbWaveInstrument(),
                _ => throw new ArgumentException("旋律の割り当てチャンネルが不正です。", nameof(channel))
            };
        }

        private static void DiagnoseProgram(MidiAllocatedNote note, Instrument instrument,
            HashSet<(int Channel, int Program)> programs, ConversionReport report)
        {
            MidiNote source = note.Source.Timing.Source;
            if (source.IsDrum || !programs.Add((source.Source.Channel, source.Program)))
            {
                return;
            }
            report.AddWarning(source.Source.Diagnose("ProgramApproximated", "GM program をチップの既存音色へ近似しました。") with
            {
                OutputTrack = note.OutputTrack, OutputTick = note.StartTick,
                Original = source.Program.ToString(CultureInfo.InvariantCulture), Converted = instrument.Name
            });
        }

        private static void ValidateReport(ConversionReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            if (report.Format != ConversionFormat.Midi)
            {
                throw new ArgumentException("MIDI レポートが必要です。", nameof(report));
            }
            if (report.Chip != ChipKind.Nes && report.Chip != ChipKind.GameBoy && report.Chip != ChipKind.Snes)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "MIDI の出力は NES / GB / SNES に対応しています。"));
            }
        }
    }
}
