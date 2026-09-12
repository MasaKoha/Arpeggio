using System;
using System.Collections.Generic;
using System.Globalization;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Export.Control;

namespace Arpeggio.Formats.Export.Nes
{
    /// <summary>NES 変換で省略・量子化する入力と、実際に発生した位相副作用を診断する。</summary>
    internal sealed class NesConversionDiagnostics
    {
        private const int MaximumVolume = 15;
        private const double VolumeErrorTolerance = 1e-9;
        private readonly ConversionReport _report;

        internal NesConversionDiagnostics(ConversionReport report)
        {
            _report = report;
        }

        internal void InspectTracks(IReadOnlyList<ControlTrack> tracks)
        {
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                ControlTrack track = tracks[trackIndex];
                if (track.Muted)
                {
                    continue;
                }
                InspectPan(track, trackIndex);
            }
        }

        internal int QuantizeVolume(ControlEvent control)
        {
            double original = MaximumVolume * control.Volume;
            int converted = (int)Math.Round(original, MidpointRounding.AwayFromZero);
            double error = Math.Abs(original - converted);
            if (error > VolumeErrorTolerance)
            {
                _report.AddWarning(ForNote("VolumeQuantized", "最終音量を NES の整数レベルへ丸めました。", control) with
                {
                    Original = Format(original), Converted = Format(converted), MaximumError = error
                });
            }
            return converted;
        }

        internal void InspectTriangleVolume(ControlEvent control)
        {
            ControlNote note = control.Note!;
            bool hasVolumeSlide = false;
            string original = Format(note.Volume);
            foreach (NoteEffect effect in note.Effects)
            {
                if (effect.Kind == NoteEffectKind.VolumeSlide)
                {
                    hasVolumeSlide = true;
                    original = $"volume={Format(note.Volume)}; VolumeSlide={Format(effect.Value)}";
                    break;
                }
            }
            if (note.Volume == MaximumVolume && !hasVolumeSlide)
            {
                return;
            }
            _report.AddWarning(ForNote("TriangleVolumeIgnored", "Triangle は固定音量のため、ノート音量と VolumeSlide を反映できません。", control) with
            {
                Original = original,
                Converted = Format(MaximumVolume)
            });
        }

        internal void ReportPulsePhaseRestart(ControlEvent control, int originalHigh, int convertedHigh)
        {
            _report.AddWarning(ForNote("PulsePhaseRestarted", "継続音の timer high 更新により duty sequencer の位相が再開します。timer divider は戻りません。", control) with
            {
                Original = Format(originalHigh), Converted = Format(convertedHigh)
            });
        }

        internal void ReportPitchClamp(ControlEvent control, double clampedMidiNote)
        {
            _report.AddWarning(ForNote("PitchClamped", "変調後の音程を NES チャンネルの連続音域へ制限しました。", control) with
            {
                Original = Format(control.MidiNote), Converted = Format(clampedMidiNote),
                MaximumError = Math.Abs(control.MidiNote - clampedMidiNote)
            });
        }

        private void InspectPan(ControlTrack track, int trackIndex)
        {
            if (track.Pan != 0)
            {
                _report.AddWarning(new ConversionDiagnostic("PanReduced", "NES 出力はモノラルのため、トラックのパンを中央へ統合しました。")
                {
                    SourceTrack = trackIndex, OutputTrack = trackIndex,
                    Original = Format(track.Pan), Converted = Format(0), MaximumError = Math.Abs(track.Pan)
                });
            }
        }

        internal void InspectDpcmNotes(ControlTimeline timeline)
        {
            var inspectedNotes = new HashSet<ControlNote>();
            foreach (ControlEvent control in timeline.Events)
            {
                ControlTrack track = timeline.Tracks[control.TrackIndex];
                if (track.Muted || track.Channel != ChannelKind.Dpcm || control.Kind != ControlEventKind.NoteOn)
                {
                    continue;
                }
                // SongValidator は Delay < DurationTicks を保証し、全元ノートは初回の On に現れる。
                if (inspectedNotes.Add(control.Note!))
                {
                    _report.AddError(ForNote("UnsupportedDpcm", "ミュートされていない DPCM トラックのノートは NES 書き出しに対応していません。", control));
                }
            }
        }

        private static ConversionDiagnostic ForNote(string code, string message, ControlEvent control)
        {
            ControlNote note = control.Note!;
            return new ConversionDiagnostic(code, message)
            {
                SourceTrack = control.TrackIndex, SourceEvent = note.SourceEvent, SourceTick = note.Tick,
                OutputTrack = control.TrackIndex
            };
        }

        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
