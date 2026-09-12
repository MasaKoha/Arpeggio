using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Compile;
using Arpeggio.Core.Sfx.Parameters;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Compile
{
    /// <summary>生成 Song を通常の PCM / WAV 経路へ接続し、ゼロ保持・長さ・再現性を検証する。</summary>
    public sealed class SfxCompiledAudioTests
    {
        private const int StereoChannels = 2;
        private const int ControlFramesPerSecond = 60;
        private const int ShortEnvelopeFrames = 3;
        private const int ShortBodyFrames = 4;
        private const int DspSampleRate = 32000;
        private const int ResamplingHistorySamples = 2;

        /// <summary>五つの周期波形は保存往復後も非無音・非クリップの WAV として再解析できる。</summary>
        [Theory]
        [InlineData(SnesWaveformKind.Pulse)]
        [InlineData(SnesWaveformKind.Sine)]
        [InlineData(SnesWaveformKind.Square)]
        [InlineData(SnesWaveformKind.Saw)]
        [InlineData(SnesWaveformKind.Triangle)]
        public void Compile_AllSnesWaveformsReachWav(SnesWaveformKind waveform)
        {
            const int SampleRate = 44100;
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with { Snes = parameters.Snes! with { Waveform = waveform } };
            Song song = SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song;
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            float[] samples = new SongRenderer(restored, new RenderSettings(SampleRate, 1, 0)).RenderAll();
            AssertFiniteAudibleUnclipped(samples);
            Assert.Equal(new SongRenderer(song, new RenderSettings(SampleRate, 1, 0)).RenderAll(), samples);
            AssertWav(samples, SampleRate);
        }

        /// <summary>noiseRate の端点変更が DSP ノイズの PCM へ届き、周期波形のまま鳴る接続漏れを検出する。</summary>
        [Fact]
        public void Compile_NoiseRateChangesTheRenderedSignal()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with
            {
                Tone = parameters.Tone with { Enabled = false },
                Noise = parameters.Noise with { Enabled = true },
                Snes = parameters.Snes! with { NoiseRate = 1 }
            };
            var settings = new RenderSettings(44100, 1, 0);
            float[] slow = new SongRenderer(SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song, settings).RenderAll();
            parameters = parameters with { Snes = parameters.Snes! with { NoiseRate = 31 } };
            float[] fast = new SongRenderer(SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song, settings).RenderAll();
            AssertFiniteAudibleUnclipped(slow);
            AssertFiniteAudibleUnclipped(fast);
            Assert.NotEqual(slow, fast);
        }

        /// <summary>DSP ノイズとトーンの最終ゼロは tail の有無・レート・分割バッファ・Reset で変わらない。</summary>
        [Theory]
        [InlineData(44100, false, 0)]
        [InlineData(44100, false, 0.05)]
        [InlineData(48000, false, 0)]
        [InlineData(44101, false, 0.05)]
        [InlineData(44100, true, 0)]
        [InlineData(44100, true, 0.05)]
        [InlineData(48000, true, 0.05)]
        [InlineData(44101, true, 0)]
        public void Compile_SnesTerminalHoldSurvivesTheAudioPipeline(int sampleRate, bool noiseEnabled, double tailSeconds)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            parameters = parameters with
            {
                Tone = parameters.Tone with { Enabled = !noiseEnabled },
                Noise = parameters.Noise with { Enabled = noiseEnabled }
            };
            Song song = SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song;
            var settings = new RenderSettings(sampleRate, 1, tailSeconds);
            float[] expected = new SongRenderer(song, settings).RenderAll();
            int bodySamples = (int)Math.Round(ShortBodyFrames * sampleRate / (double)ControlFramesPerSecond, MidpointRounding.AwayFromZero);
            int tailSamples = (int)Math.Round(tailSeconds * sampleRate, MidpointRounding.AwayFromZero);
            Assert.Equal((bodySamples + tailSamples) * StereoChannels, expected.Length);
            AssertFiniteAudibleUnclipped(expected);
            int zeroBoundary = (int)Math.Ceiling(ShortEnvelopeFrames * sampleRate / (double)ControlFramesPerSecond);
            int history = (int)Math.Ceiling(ResamplingHistorySamples * sampleRate / (double)DspSampleRate);
            Assert.All(expected.Skip((zeroBoundary + history) * StereoChannels), sample => Assert.Equal(0f, sample));
            Assert.Equal(0f, expected[bodySamples * StereoChannels - 1]);
            AssertWav(expected, sampleRate);
            foreach (int bufferFrames in new[] { 1, 733, 1024 })
            {
                var renderer = new SongRenderer(song, settings);
                float[] actual = RenderInChunks(renderer, expected.Length, bufferFrames);
                AssertBytesEqual(expected, actual);
                renderer.Reset();
                AssertBytesEqual(expected, renderer.RenderAll());
            }
        }

        /// <summary>SFX decay は数十 msを越えても DSP release の約8 msへ短縮されない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Compile_SnesLongDecayRemainsAudibleUntilTheMacroFades(bool noiseEnabled)
        {
            const int SampleRate = 48000;
            const int SamplesPerControlFrame = SampleRate / ControlFramesPerSecond;
            const int LateDecayFrame = 18;
            const int ZeroFrame = 24;
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Snes);
            var envelope = new SfxEnvelopeParameters { SustainSeconds = 0, DecaySeconds = 0.4 };
            parameters = parameters with
            {
                Tone = parameters.Tone with { Enabled = !noiseEnabled, Envelope = envelope },
                Noise = parameters.Noise with { Enabled = noiseEnabled, Envelope = envelope }
            };
            Song song = SfxSongCompiler.Compile(parameters, ChipKind.Snes).Song;
            var instrument = Assert.IsType<SnesSampleInstrument>(Assert.Single(song.Instruments));
            Assert.Equal(0, instrument.Envelope.ReleaseSeconds);
            Assert.Equal(3, instrument.VolumeMacro!.Values[LateDecayFrame]);
            Assert.Equal(50, song.LengthTicks);
            float[] samples = new SongRenderer(song, new RenderSettings(SampleRate, 1, 0.05)).RenderAll();
            Assert.Contains(samples.Skip(LateDecayFrame * SamplesPerControlFrame * StereoChannels)
                .Take(SamplesPerControlFrame * StereoChannels), sample => sample != 0);
            int history = (int)Math.Ceiling(ResamplingHistorySamples * SampleRate / (double)DspSampleRate);
            Assert.All(samples.Skip((ZeroFrame * SamplesPerControlFrame + history) * StereoChannels), sample => Assert.Equal(0f, sample));
        }

        /// <summary>三チップとも二声の生成音は有限で、全レイヤー音量0だけは意図した無音になる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Compile_TwoLayersAndExplicitSilenceReachWav(ChipKind chip)
        {
            const int SampleRate = 44100;
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with { Noise = parameters.Noise with { Enabled = true } };
            SfxSongCompilationResult audible = SfxSongCompiler.Compile(parameters, chip);
            float[] samples = new SongRenderer(audible.Song, new RenderSettings(SampleRate, 1, 0)).RenderAll();
            AssertFiniteAudibleUnclipped(samples);
            AssertWav(samples, SampleRate);
            parameters = parameters with
            {
                Tone = parameters.Tone with { Envelope = parameters.Tone.Envelope with { Volume = 0 } },
                Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { Volume = 0 } }
            };
            SfxSongCompilationResult silent = SfxSongCompiler.Compile(parameters, chip);
            Assert.Equal(audible.Song.LengthTicks, silent.Song.LengthTicks);
            Assert.Single(silent.Warnings, warning => warning.Code == "SilentParameters");
            float[] zeros = new SongRenderer(silent.Song, new RenderSettings(SampleRate, 1, 0)).RenderAll();
            Assert.All(zeros, sample => Assert.Equal(0f, sample));
        }

        private static float[] RenderInChunks(SongRenderer renderer, int sampleCount, int bufferFrames)
        {
            var samples = new float[sampleCount];
            int offset = 0;
            while (offset < samples.Length)
            {
                int count = Math.Min(bufferFrames * StereoChannels, samples.Length - offset);
                int rendered = renderer.Render(samples.AsSpan(offset, count));
                Assert.True(rendered > 0);
                offset += rendered * StereoChannels;
            }
            Assert.True(renderer.IsFinished);
            return samples;
        }

        private static void AssertFiniteAudibleUnclipped(float[] samples)
        {
            Assert.Contains(samples, sample => sample != 0);
            Assert.All(samples, sample =>
            {
                Assert.True(float.IsFinite(sample));
                Assert.True(Math.Abs(sample) < 1);
            });
        }

        private static void AssertWav(float[] samples, int sampleRate)
        {
            using var stream = new MemoryStream();
            WavWriter.Write(stream, samples, sampleRate);
            stream.Position = 0;
            float[] decoded = WavReader.Read(stream, out int actualSampleRate);
            Assert.Equal(sampleRate, actualSampleRate);
            Assert.Equal(samples.Length, decoded.Length);
            AssertFiniteAudibleUnclipped(decoded);
            AnalysisReport report = AudioAnalyzer.Analyze(decoded, sampleRate, new AnalysisSettings());
            Assert.Equal(samples.Length / (double)(StereoChannels * sampleRate), report.DurationSeconds, 10);
            Assert.Equal(0, report.ClippedSampleCount);
            Assert.True(report.RmsDbfs > -60);
        }

        private static void AssertBytesEqual(float[] expected, float[] actual)
        {
            Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
        }
    }
}
