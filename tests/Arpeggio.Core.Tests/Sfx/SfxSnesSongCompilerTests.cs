using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>SNES 固有の周期波形・DSP ノイズ・全量保持 ADSR と音域診断を検証する。</summary>
    public sealed class SfxSnesSongCompilerTests
    {
        private const int DspSampleRate = 32000;
        private const int WaveformLength = 128;
        private const int UnityPitch = 4096;
        private const int MaximumPitch = 16383;

        /// <summary>五つの内蔵周期波形を voice0 へ割り当て、全量保持の DSP 設定と有限マクロを保存する。</summary>
        [Theory]
        [InlineData(SnesWaveformKind.Pulse)]
        [InlineData(SnesWaveformKind.Sine)]
        [InlineData(SnesWaveformKind.Square)]
        [InlineData(SnesWaveformKind.Saw)]
        [InlineData(SnesWaveformKind.Triangle)]
        public void Compile_MapsAllPeriodicWaveforms(SnesWaveformKind waveform)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with { Snes = parameters.Snes! with { Waveform = waveform } };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Snes, "SNES SFX");
            Song song = result.Song;
            SongValidator.Validate(song);
            Assert.Equal("SNES SFX", song.Title);
            Assert.Equal(1, song.Version);
            Assert.Equal(150, song.TempoBpm);
            Assert.Equal(48, song.TicksPerBeat);
            Assert.Equal(8, song.LengthTicks);
            Assert.Equal(0, song.LoopStartTick);
            Assert.Equal(8, song.Tracks.Count);
            Assert.Equal(0, result.ToneTrackIndex);
            Assert.Null(result.NoiseTrackIndex);
            for (int index = 0; index < song.Tracks.Count; index++)
            {
                Track track = song.Tracks[index];
                Assert.Equal(ChannelKind.Sample, track.Channel);
                Assert.Equal(index, track.ChannelIndex);
                Assert.Equal($"Sample {index + 1}", track.Name);
                Assert.False(track.Muted);
                Assert.Equal(0, track.Pan);
                Assert.Null(track.DefaultInstrumentId);
            }
            Assert.All(song.Tracks.Skip(1), track => Assert.Empty(track.Notes));
            Note note = Assert.Single(song.Tracks[0].Notes);
            Assert.Equal(1, note.InstrumentId);
            Assert.Equal(69, note.MidiNote);
            Assert.Equal(0, note.Tick);
            Assert.Equal(8, note.DurationTicks);
            Assert.Equal(15, note.Volume);
            Assert.Empty(note.Effects);
            var instrument = Assert.IsType<SnesSampleInstrument>(Assert.Single(song.Instruments));
            Assert.Equal(1, instrument.Id);
            Assert.Equal("sfx-tone", instrument.Name);
            Assert.Equal(waveform, instrument.Waveform);
            Assert.False(instrument.NoiseEnabled);
            AssertCommonInstrument(instrument);
            Assert.Equal(new[] { 0, 0, 0, 0 }, instrument.PitchMacro!.Values);
            Assert.Equal(new[] { 0, 0, 0, 0 }, instrument.ArpeggioMacro!.Values);
            Assert.Equal(-1, instrument.PitchMacro.LoopIndex);
            Assert.Equal(-1, instrument.ArpeggioMacro.LoopIndex);
            Assert.Equal(0, song.SnesEcho.DelayMilliseconds);
            Assert.Equal(0, song.SnesEcho.Feedback);
            Assert.Equal(0, song.SnesEcho.Volume);
            Assert.Equal(new[] { 127, 0, 0, 0, 0, 0, 0, 0 }, song.SnesEcho.FirCoefficients);
            string serialized = SongSerializer.Serialize(song);
            Assert.Equal(serialized, SongSerializer.Serialize(SongSerializer.Deserialize(serialized)));
        }

        /// <summary>ノイズだけでも ID2 / voice1 を維持し、全31 rateを固定 DSP ノイズへ渡す。</summary>
        [Fact]
        public void Compile_MapsEveryNoiseRateWithoutToneMacros()
        {
            for (int rate = 1; rate <= 31; rate++)
            {
                SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
                parameters = parameters with
                {
                    Tone = parameters.Tone with { Enabled = false, BaseFrequencyHz = 12000, SlideSemitonesPerSecond = 360 },
                    Noise = parameters.Noise with { Enabled = true },
                    Snes = parameters.Snes! with { NoiseRate = rate }
                };
                SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Snes);
                Assert.Null(result.ToneTrackIndex);
                Assert.Equal(1, result.NoiseTrackIndex);
                Assert.Empty(result.TonePitchFrames);
                Assert.DoesNotContain(result.Warnings, warning => warning.Layer == "tone");
                Assert.Empty(result.Song.Tracks[0].Notes);
                var noise = Assert.IsType<SnesSampleInstrument>(Assert.Single(result.Song.Instruments));
                Assert.Equal(2, noise.Id);
                Assert.Equal("sfx-noise", noise.Name);
                Assert.True(noise.NoiseEnabled);
                Assert.Equal(rate, noise.NoiseRate);
                Assert.Equal(SnesWaveformKind.Sine, noise.Waveform);
                Assert.Null(noise.PitchMacro);
                Assert.Null(noise.ArpeggioMacro);
                AssertCommonInstrument(noise);
                Note note = Assert.Single(result.Song.Tracks[1].Notes);
                Assert.Equal(2, note.InstrumentId);
                Assert.Equal(60, note.MidiNote);
                Assert.Equal(0, note.Tick);
                Assert.Equal(8, note.DurationTicks);
                Assert.Equal(15, note.Volume);
                Assert.Empty(note.Effects);
                Assert.All(result.Song.Tracks.Skip(2), track => Assert.Empty(track.Notes));
                SongValidator.Validate(result.Song);
            }
        }

        /// <summary>基準音が有効でも slide の途中で約1000 Hzへ制限され、再生レジスタと全軌跡診断が一致する。</summary>
        [Fact]
        public void Compile_UsesFourteenBitLimitForTheEntireTone()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with { Tone = parameters.Tone with { SlideSemitonesPerSecond = 360 } };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Snes);
            double maximumFrequency = (double)DspSampleRate / WaveformLength * MaximumPitch / UnityPitch;
            Assert.InRange(maximumFrequency, 999.9, 1000);
            SfxGenerationWarning warning = Assert.Single(result.Warnings, candidate => candidate.Code == "PitchClamped");
            Assert.Equal(3, warning.FromFrame);
            Assert.Equal(3, warning.ToFrame);
            Assert.Equal(maximumFrequency, warning.Actual);
            var voice = new SnesVoiceSynthesizer();
            Note note = result.Song.Tracks[0].Notes[0];
            voice.NoteOn(note.MidiNote, note.Volume, result.Song.Instruments[0], note.Effects);
            int[] expectedRegisters = { 7209, 10195, 14418, MaximumPitch };
            for (int frame = 0; frame < expectedRegisters.Length; frame++)
            {
                Assert.Equal(expectedRegisters[frame], voice.PitchRegister);
                Assert.Equal((double)DspSampleRate / WaveformLength * voice.PitchRegister / UnityPitch,
                    result.TonePitchFrames[frame].ActualFrequencyHz, 10);
                voice.AdvanceFrame();
            }
        }

        /// <summary>高い基準 Hz でも要求値と保存マクロを維持して、先頭から上限を診断する。</summary>
        [Theory]
        [InlineData(1001)]
        [InlineData(12000)]
        public void Compile_ReportsHighBaseFrequency(double frequency)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with { Tone = parameters.Tone with { BaseFrequencyHz = frequency } };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Snes);
            Assert.Equal(frequency, result.Curves.Parameters.Tone.BaseFrequencyHz);
            Assert.All(result.TonePitchFrames, frame => Assert.True(frame.IsClamped));
            SfxGenerationWarning warning = Assert.Single(result.Warnings, candidate => candidate.Code == "PitchClamped");
            Assert.Equal(0, warning.FromFrame);
            Assert.Equal(3, warning.ToFrame);
        }

        /// <summary>1000 Hz要求がセント丸めで上限内へ入る場合は、保存列の実際の要求音程を診断する。</summary>
        [Fact]
        public void Compile_DiagnosesTheSavedCentCurveAtTheLimit()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with { Tone = parameters.Tone with { BaseFrequencyHz = 1000 } };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, ChipKind.Snes);
            Assert.Equal(1000, result.Curves.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(83.21, result.TonePitchFrames[0].RequestedMidiNote, 10);
            Assert.False(result.TonePitchFrames[0].IsClamped);
            Assert.DoesNotContain(result.Warnings, warning => warning.Code == "PitchClamped");
        }

        /// <summary>SNES の保存列・音量配列・エコー係数は再生成ごとに独立し、入力を変更しない。</summary>
        [Fact]
        public void Compile_IsolatesSnesGenerationResults()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with { Noise = parameters.Noise with { Enabled = true } };
            SfxSongCompilationResult first = SfxSongCompiler.Compile(parameters, ChipKind.Snes);
            SfxSongCompilationResult second = SfxSongCompiler.Compile(parameters, ChipKind.Snes);
            string expected = SongSerializer.Serialize(second.Song);
            Assert.Equal(expected, SongSerializer.Serialize(first.Song));
            Assert.Equal(first.TonePitchFrames, second.TonePitchFrames);
            Assert.Equal(first.Warnings, second.Warnings);
            var tone = Assert.IsType<SnesSampleInstrument>(first.Song.Instruments[0]);
            var noise = Assert.IsType<SnesSampleInstrument>(first.Song.Instruments[1]);
            tone.VolumeMacro!.Values[0] = 0;
            tone.PitchMacro!.Values[0] = 100;
            noise.VolumeMacro!.Values[0] = 0;
            first.Song.SnesEcho.FirCoefficients[0] = 0;
            Assert.Equal(expected, SongSerializer.Serialize(second.Song));
            Assert.Equal(12, parameters.Tone.Envelope.Volume);
            Assert.Equal(12, parameters.Noise.Envelope.Volume);
        }

        /// <summary>トーン変化がノイズ設定・PCMへ漏れず、独立した包絡終端を維持する。</summary>
        [Fact]
        public void Compile_KeepsNoiseIndependentFromTone()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with
            {
                Noise = parameters.Noise with
                {
                    Enabled = true, Envelope = parameters.Noise.Envelope with { SustainSeconds = 0.05, DecaySeconds = 0.15 }
                }
            };
            Song first = SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song;
            parameters = parameters with
            {
                Tone = parameters.Tone with
                {
                    SlideSemitonesPerSecond = 360, DeltaSlideSemitonesPerSecondSquared = 1440,
                    VibratoDepthCents = 200, VibratoSpeedHz = 20, PitchChangeSemitones = 24,
                    PitchChangeTimeSeconds = 0, RepeatPeriodSeconds = 1.0 / 60
                }
            };
            Song second = SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song;
            Assert.Equal(new[] { 1, 2 }, first.Instruments.Select(instrument => instrument.Id));
            Assert.Equal(8, first.Tracks[0].Notes[0].DurationTicks);
            Assert.Equal(26, first.Tracks[1].Notes[0].DurationTicks);
            Assert.Equal(26, first.LengthTicks);
            Assert.NotSame(SfxCompilationTestData.Volume(first.Instruments[0]), SfxCompilationTestData.Volume(first.Instruments[1]));
            first.Tracks[0].Muted = true;
            second.Tracks[0].Muted = true;
            var settings = new RenderSettings(44100, 1, 0);
            Assert.Equal(new SongRenderer(first, settings).RenderAll(), new SongRenderer(second, settings).RenderAll());
        }

        private static void AssertCommonInstrument(SnesSampleInstrument instrument)
        {
            Assert.Null(instrument.Preset);
            Assert.Null(instrument.SampleData);
            Assert.True(instrument.Loop);
            Assert.False(instrument.PitchModulation);
            Assert.Equal(0, instrument.Pan);
            Assert.Equal(0, instrument.EchoSend);
            Assert.Equal(new AdsrEnvelope(0, 0, 1, 0), instrument.Envelope);
            Assert.Equal(new SnesAdsrRegisters(15, 0, 7, 0), instrument.AdsrRegisters);
            Assert.NotNull(instrument.VolumeMacro);
            Assert.Equal(new[] { 12, 8, 4, 0 }, instrument.VolumeMacro.Values);
            Assert.Equal(-1, instrument.VolumeMacro.LoopIndex);
        }
    }
}
