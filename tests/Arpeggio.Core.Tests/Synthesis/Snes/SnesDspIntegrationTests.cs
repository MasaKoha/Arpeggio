using System;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>ノイズ・ピッチ変調と内部 DSP 時間軸を実際のレンダラー経由で検証する。</summary>
    public sealed class SnesDspIntegrationTests
    {
        private const int SampleRate = 32000;
        private const int StereoChannels = 2;
        private const int MaximumVolume = 15;

        /// <summary>同じ入力のノイズは完全一致し、高レートほどスペクトル重心が高くなる。</summary>
        [Fact]
        public void Noise_IsDeterministicAndRateRaisesCentroid()
        {
            float[] slow = RenderNoise(12);
            float[] fast = RenderNoise(31);
            Assert.Equal(fast, RenderNoise(31));
            AnalysisSettings settings = new AnalysisSettings(100, -60, 2048);
            AnalysisReport slowReport = AudioAnalyzer.Analyze(slow, SampleRate, settings);
            AnalysisReport fastReport = AudioAnalyzer.Analyze(fast, SampleRate, settings);
            Assert.True(MeanCentroid(fastReport) > MeanCentroid(slowReport) * 2);
            Assert.True(fastReport.RmsDbfs > -40);
        }

        /// <summary>レート 0 は LFSR を停止し、Reset は初期列を再現する。</summary>
        [Fact]
        public void Noise_ResetAndStoppedClockAreDeterministic()
        {
            SnesNoiseGenerator noise = new SnesNoiseGenerator();
            double stopped = noise.ReadSample(0);
            for (int index = 0; index < 100; index++)
            {
                Assert.Equal(stopped, noise.ReadSample(0));
            }
            double first = noise.ReadSample(31);
            Assert.NotEqual(stopped, first);
            noise.Reset();
            Assert.Equal(first, noise.ReadSample(31));
        }

        /// <summary>ミュートした低周波の前ボイスも変調源として動き、後ボイスの窓ごとの音程が揺れる。</summary>
        [Fact]
        public void PitchModulation_IncreasesWindowFrequencyVarianceThroughMixer()
        {
            Song plain = CreateModulatedSong(false);
            Song modulated = CreateModulatedSong(true);
            RenderSettings renderSettings = new RenderSettings(SampleRate, 1, 0);
            float[] plainSamples = new SongRenderer(plain, renderSettings).RenderAll();
            float[] modulatedSamples = new SongRenderer(modulated, renderSettings).RenderAll();
            AnalysisSettings analysisSettings = new AnalysisSettings(25, -60, 1024);
            AnalysisReport plainReport = AudioAnalyzer.Analyze(plainSamples, SampleRate, analysisSettings);
            AnalysisReport modulatedReport = AudioAnalyzer.Analyze(modulatedSamples, SampleRate, analysisSettings);
            Assert.True(FrequencyVariance(modulatedReport) > FrequencyVariance(plainReport) + 1000);
        }

        /// <summary>変調の負端はピッチ 0、正端は 14 bit 上限へ飽和する。</summary>
        [Fact]
        public void PitchModulation_ClampsRegisterEndpoints()
        {
            Assert.Equal(0, SnesVoiceSynthesizer.ModulatePitch(4096, short.MinValue));
            Assert.Equal(4096, SnesVoiceSynthesizer.ModulatePitch(4096, 0));
            Assert.Equal(16383, SnesVoiceSynthesizer.ModulatePitch(16383, short.MaxValue));
        }

        /// <summary>音色レジスタは遅い秒指定より優先する。</summary>
        [Fact]
        public void AdsrRegisters_TakePriorityOverSeconds()
        {
            SnesSampleInstrument instrument = new SnesSampleInstrument
            {
                Envelope = new AdsrEnvelope(10, 10, 0, 10),
                AdsrRegisters = new SnesAdsrRegisters(15, 7, 7, 0)
            };
            SnesVoiceSynthesizer voice = new SnesVoiceSynthesizer(SampleRate);
            voice.NoteOn(69, MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            float[] samples = new float[1024];
            voice.Render(samples);
            Assert.Contains(samples, value => Math.Abs(value) > 0.5);
        }

        /// <summary>変調・ノイズ・FIR を同時に有効にしても分割 Render は確保せず、結果も一致する。</summary>
        [Theory]
        [InlineData(22050)]
        [InlineData(44100)]
        [InlineData(48000)]
        public void Render_DspFeaturesAreChunkInvariantAndAllocateZeroBytes(int outputRate)
        {
            Song song = CreateModulatedSong(true);
            ((SnesSampleInstrument)song.Instruments[0]).NoiseEnabled = true;
            ((SnesSampleInstrument)song.Instruments[0]).NoiseRate = 16;
            ((SnesSampleInstrument)song.Instruments[1]).EchoSend = 0.5;
            song.SnesEcho = new SnesEchoSettings
            {
                DelayMilliseconds = 16, Feedback = 0.25, Volume = 0.5,
                FirCoefficients = SnesEchoFirPresets.LowPass
            };
            RenderSettings settings = new RenderSettings(outputRate, 1, 0);
            float[] expected = new SongRenderer(song, settings).RenderAll();
            SongRenderer renderer = new SongRenderer(song, settings);
            float[] actual = new float[expected.Length];
            // 静的テーブルの初回初期化（ADSR の decay 段・FIR エコー・ノイズ）が計測区間に入らないよう、曲全体を一度鳴らす
            float[] warmup = new float[512];
            while (!renderer.IsFinished)
            {
                renderer.Render(warmup);
            }
            // Reset で合成器が作り直されるため、計測と同じチャンク割りで 2 周目も鳴らし、Reset 後にだけ通る経路も温めておく
            renderer.Reset();
            RenderInChunks(renderer, actual);
            renderer.Reset();
            long before = GC.GetAllocatedBytesForCurrentThread();
            RenderInChunks(renderer, actual);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
            Assert.Equal(expected, actual);
        }

        private static void RenderInChunks(SongRenderer renderer, float[] destination)
        {
            const int ChunkValues = 514;
            int position = 0;
            while (position < destination.Length)
            {
                int count = Math.Min(ChunkValues, destination.Length - position);
                int rendered = renderer.Render(destination.AsSpan(position, count));
                position += rendered * StereoChannels;
            }
        }

        private static float[] RenderNoise(int rate)
        {
            SnesVoiceSynthesizer voice = new SnesVoiceSynthesizer(SampleRate);
            voice.NoteOn(60, MaximumVolume, new SnesSampleInstrument { NoiseEnabled = true, NoiseRate = rate }, ReadOnlySpan<NoteEffect>.Empty);
            float[] mono = new float[SampleRate];
            voice.Render(mono);
            float[] stereo = new float[SampleRate * StereoChannels];
            for (int index = 0; index < mono.Length; index++)
            {
                stereo[index * StereoChannels] = mono[index];
                stereo[index * StereoChannels + 1] = mono[index];
            }
            return stereo;
        }

        private static Song CreateModulatedSong(bool modulation)
        {
            const int DurationTicks = 192;
            Song song = SongFactory.Create(ChipKind.Snes, lengthTicks: DurationTicks);
            song.Instruments.Add(new SnesSampleInstrument { Id = 2, PitchModulation = modulation });
            song.Tracks[0].Muted = true;
            song.Tracks[0].Notes.Add(new Note { MidiNote = 0, DurationTicks = DurationTicks, InstrumentId = 1 });
            song.Tracks[1].Notes.Add(new Note { MidiNote = 69, DurationTicks = DurationTicks, InstrumentId = 2 });
            return song;
        }

        private static double MeanCentroid(AnalysisReport report)
        {
            double total = 0;
            foreach (AnalysisWindow window in report.Windows)
            {
                total += window.SpectralCentroidHz;
            }
            return total / report.Windows.Count;
        }

        private static double FrequencyVariance(AnalysisReport report)
        {
            double sum = 0;
            double sumSquares = 0;
            foreach (AnalysisWindow window in report.Windows)
            {
                sum += window.DominantFrequencyHz;
                sumSquares += window.DominantFrequencyHz * window.DominantFrequencyHz;
            }
            double mean = sum / report.Windows.Count;
            return sumSquares / report.Windows.Count - mean * mean;
        }
    }
}
