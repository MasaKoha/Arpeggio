using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Curves;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Compile
{
    /// <summary>SFX パラメータを有限マクロと一度の発音へ展開し、通常の Song を純粋生成する。</summary>
    public static class SfxSongCompiler
    {
        internal const int ToneInstrumentId = 1;
        internal const int NoiseInstrumentId = 2;
        internal const int ToneTrackIndex = 0;
        internal const int NoteVolume = 15;
        internal const string ToneInstrumentName = "sfx-tone";
        internal const string NoiseInstrumentName = "sfx-noise";
        private const int PulseChipNoiseTrackIndex = 3;
        private const int SnesNoiseTrackIndex = 1;

        /// <summary>入力を変更せず、検証済みの新規 Song と全軌跡の生成診断を返す。</summary>
        public static SfxSongCompilationResult Compile(SfxParameters parameters, ChipKind chip, string title = "")
        {
            SfxCurveGenerationResult curves = SfxCurveGenerator.Generate(parameters, chip);
            Song song = SongFactory.Create(chip, SfxCurveGenerator.TempoBpm, curves.LengthTicks);
            song.Title = title;
            song.TicksPerBeat = Song.FixedTicksPerBeat;
            song.LoopStartTick = 0;
            song.Instruments.Clear();
            var warnings = new List<SfxGenerationWarning>(curves.Warnings);
            if (chip == ChipKind.Snes)
            {
                SfxSnesSongBuilder.Build(song, curves);
            }
            else
            {
                SfxPulseSongBuilder.Build(song, curves, warnings);
            }
            IReadOnlyList<SfxPitchFrame> pitchFrames = SfxPitchDiagnostics.Generate(curves.Tone, chip, warnings);
            SongValidator.Validate(song);
            return new SfxSongCompilationResult(song, curves, pitchFrames, warnings.AsReadOnly());
        }

        internal static int GetNoiseTrackIndex(ChipKind chip)
            => chip == ChipKind.Snes ? SnesNoiseTrackIndex : PulseChipNoiseTrackIndex;

        internal static void AddLayer(Song song, Instrument instrument, int trackIndex,
            int midiNote, SfxEnvelopeCurve envelope)
        {
            song.Instruments.Add(instrument);
            song.Tracks[trackIndex].Notes.Add(new Note
            {
                Tick = 0, DurationTicks = envelope.DurationTicks, MidiNote = midiNote,
                Volume = NoteVolume, InstrumentId = instrument.Id, Effects = Array.Empty<NoteEffect>()
            });
        }
    }
}
