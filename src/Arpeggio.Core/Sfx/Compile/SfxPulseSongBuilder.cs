using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Curves;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Compile
{
    /// <summary>NES / GB の固定パルス・ノイズへ共通曲線とチップ設定を接続する。</summary>
    internal static class SfxPulseSongBuilder
    {
        internal static void Build(Song song, SfxCurveGenerationResult curves, List<SfxGenerationWarning> warnings)
        {
            if (curves.Tone is SfxToneCurve tone)
            {
                Instrument instrument = CreateTone(song.Chip, curves.Parameters, tone, warnings);
                SfxSongCompiler.AddLayer(song, instrument, SfxSongCompiler.ToneTrackIndex,
                    tone.AnchorMidiNote, tone.Envelope);
            }
            if (curves.Noise is SfxEnvelopeCurve noise)
            {
                int selection = song.Chip == ChipKind.Nes
                    ? curves.Parameters.Nes!.NoisePeriodIndex : curves.Parameters.GameBoy!.NoiseSelection;
                Instrument instrument = CreateNoise(song.Chip, curves.Parameters, noise, warnings);
                SfxSongCompiler.AddLayer(song, instrument, SfxSongCompiler.GetNoiseTrackIndex(song.Chip), selection, noise);
            }
        }

        private static Instrument CreateTone(ChipKind chip, SfxParameters parameters, SfxToneCurve tone,
            List<SfxGenerationWarning> warnings)
        {
            if (chip == ChipKind.Nes)
            {
                SfxNesParameters settings = parameters.Nes!;
                Macro duty = SfxDutyCurveGenerator.Generate(tone, settings.DutyPercent,
                    settings.DutySweepPercentPerSecond, "nes", warnings);
                return new NesPulseInstrument
                {
                    Id = SfxSongCompiler.ToneInstrumentId, Name = SfxSongCompiler.ToneInstrumentName,
                    Duty = (DutyCycle)duty.Values[0], DutyMacro = duty,
                    VolumeMacro = tone.Envelope.VolumeMacro, PitchMacro = tone.PitchMacro, ArpeggioMacro = tone.ArpeggioMacro
                };
            }
            SfxGameBoyParameters gameBoy = parameters.GameBoy!;
            Macro gameBoyDuty = SfxDutyCurveGenerator.Generate(tone, gameBoy.DutyPercent,
                gameBoy.DutySweepPercentPerSecond, "gameBoy", warnings);
            return new GbPulseInstrument
            {
                Id = SfxSongCompiler.ToneInstrumentId, Name = SfxSongCompiler.ToneInstrumentName,
                Duty = (DutyCycle)gameBoyDuty.Values[0], DutyMacro = gameBoyDuty,
                InitialVolume = SfxSongCompiler.NoteVolume, EnvelopeIncreasing = false, EnvelopeStepFrames = 0,
                VolumeMacro = tone.Envelope.VolumeMacro, PitchMacro = tone.PitchMacro, ArpeggioMacro = tone.ArpeggioMacro
            };
        }

        private static Instrument CreateNoise(ChipKind chip, SfxParameters parameters, SfxEnvelopeCurve noise,
            List<SfxGenerationWarning> warnings)
        {
            if (chip == ChipKind.Nes)
            {
                SfxNesParameters settings = parameters.Nes!;
                return new NesNoiseInstrument
                {
                    Id = SfxSongCompiler.NoiseInstrumentId, Name = SfxSongCompiler.NoiseInstrumentName,
                    NoiseMode = settings.NoiseMode, VolumeMacro = noise.VolumeMacro,
                    PitchMacro = SfxNoiseCurveGenerator.Generate(noise, settings.NoisePeriodIndex,
                        settings.NoiseSlideIndicesPerSecond, SfxNoiseCurveGenerator.MaximumNesSelection,
                        "nes.noisePeriodIndex", warnings)
                };
            }
            SfxGameBoyParameters gameBoy = parameters.GameBoy!;
            return new GbNoiseInstrument
            {
                Id = SfxSongCompiler.NoiseInstrumentId, Name = SfxSongCompiler.NoiseInstrumentName,
                LfsrWidth = gameBoy.NoiseWidth, VolumeMacro = noise.VolumeMacro,
                PitchMacro = SfxNoiseCurveGenerator.Generate(noise, gameBoy.NoiseSelection,
                    gameBoy.NoiseSlideSelectionsPerSecond, SfxNoiseCurveGenerator.MaximumGameBoySelection,
                    "gameBoy.noiseSelection", warnings)
            };
        }
    }
}
