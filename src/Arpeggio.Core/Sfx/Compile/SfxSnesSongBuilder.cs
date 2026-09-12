using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx.Curves;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Sfx.Compile
{
    /// <summary>SNES の内蔵波形と DSP ノイズを、全量保持 ADSR と自由音量マクロで組み立てる。</summary>
    internal static class SfxSnesSongBuilder
    {
        private const int NoiseMidiNote = 60;
        private const int FastestAttack = 15;
        private const int FullSustainLevel = 7;
        private const int DefaultNoiseRate = 31;
        private const int DefaultSampleRate = 44100;
        private const int DefaultRootMidiNote = 60;

        internal static void Build(Song song, SfxCurveGenerationResult curves)
        {
            SfxSnesParameters settings = curves.Parameters.Snes!;
            song.SnesEcho = new SnesEchoSettings
            {
                DelayMilliseconds = 0, Feedback = 0, Volume = 0, FirCoefficients = SnesEchoFirPresets.Flat
            };
            if (curves.Tone is SfxToneCurve tone)
            {
                SnesSampleInstrument instrument = CreateInstrument(tone.Envelope,
                    SfxSongCompiler.ToneInstrumentId, SfxSongCompiler.ToneInstrumentName);
                instrument.Waveform = settings.Waveform;
                instrument.PitchMacro = tone.PitchMacro;
                instrument.ArpeggioMacro = tone.ArpeggioMacro;
                SfxSongCompiler.AddLayer(song, instrument, SfxSongCompiler.ToneTrackIndex,
                    tone.AnchorMidiNote, tone.Envelope);
            }
            if (curves.Noise is SfxEnvelopeCurve noise)
            {
                SnesSampleInstrument instrument = CreateInstrument(noise,
                    SfxSongCompiler.NoiseInstrumentId, SfxSongCompiler.NoiseInstrumentName);
                instrument.NoiseEnabled = true;
                instrument.NoiseRate = settings.NoiseRate;
                SfxSongCompiler.AddLayer(song, instrument, SfxSongCompiler.GetNoiseTrackIndex(song.Chip), NoiseMidiNote, noise);
            }
        }

        private static SnesSampleInstrument CreateInstrument(SfxEnvelopeCurve envelope, int identifier, string name)
        {
            // SFX の減衰秒数を DSP release へ写すと約8 msに化けるため、包絡は音量マクロだけへ渡す。
            return new SnesSampleInstrument
            {
                Id = identifier, Name = name, Waveform = SnesWaveformKind.Sine,
                Preset = null, SampleData = null, Loop = true,
                Envelope = new AdsrEnvelope(0, 0, 1, 0),
                AdsrRegisters = new SnesAdsrRegisters(FastestAttack, 0, FullSustainLevel, 0),
                VolumeMacro = envelope.VolumeMacro, PitchMacro = null, ArpeggioMacro = null,
                Pan = 0, EchoSend = 0, PitchModulation = false, NoiseEnabled = false, NoiseRate = DefaultNoiseRate,
                SampleRate = DefaultSampleRate, RootMidiNote = DefaultRootMidiNote, LoopStart = 0, LoopEnd = 0
            };
        }
    }
}
