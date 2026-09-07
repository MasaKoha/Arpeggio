using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Render;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Instruments.Snes
{
    /// <summary>内蔵バンクの素材・音程・スペクトル・音声経路の確保量を検証する。</summary>
    public sealed class SnesInstrumentBankTests
    {
        private const int SampleRate = 32000;
        private const int StereoChannels = 2;
        private const int MiddleC = 60;
        private const int MaximumVolume = 15;
        private const double MiddleCFrequency = 261.6255653005986;
        private const double SemitonesPerOctave = 12;
        private const int MinimumLoopLength = 64;
        private const int MaximumLoopLength = 512;
        private const int BrrBlockLength = 16;
        private const double AudibleThresholdDb = -60;
        private const double MaximumKickCentroid = 300;
        private const double MinimumHatCentroid = 4000;

        /// <summary>全プリセットを独立したテストケースにする。</summary>
        public static IEnumerable<object[]> Presets => SnesInstrumentCatalog.All.Select(preset => new object[] { preset.Name });

        /// <summary>二回の生成がビット一致し、分類ごとの長さとループ境界を満たす。</summary>
        [Theory]
        [MemberData(nameof(Presets))]
        public void Build_IsDeterministicAndHasValidBoundaries(string preset)
        {
            SnesInstrumentPreset definition = SnesInstrumentCatalog.Get(preset);
            BrrSample first = SnesInstrumentBank.Build(preset);
            BrrSample second = SnesInstrumentBank.Build(preset);
            Assert.Equal(first.Samples.ToArray(), second.Samples.ToArray());
            Assert.Equal(definition.SampleCount, first.OriginalLength);
            Assert.Equal(definition.SampleCount, first.Samples.Length);
            Assert.Equal(definition.Loop, first.Loop);
            Assert.Equal(0, first.LoopStart);
            Assert.Equal(first.Samples.Length, first.LoopEnd);
            Assert.Equal(0, first.LoopEnd % BrrBlockLength);
            if (definition.Loop)
            {
                Assert.InRange(first.Samples.Length, MinimumLoopLength, MaximumLoopLength);
            }
            else if (definition.Category == "減衰系")
            {
                Assert.InRange(first.Samples.Length, SampleRate * 3 / 10, SampleRate);
            }
            else
            {
                Assert.Equal(ExpectedDrumLength(preset), first.Samples.Length);
            }
            Assert.All(first.Samples.ToArray(), sample => Assert.InRange(sample, -0.9f, 0.9f));
        }

        /// <summary>一秒の C4 を DSP 経由で解析し、非無音・無クリップ・音程またはドラム帯域を確認する。</summary>
        [Theory]
        [MemberData(nameof(Presets))]
        public void Render_IsAudibleUnclippedAndHasExpectedSpectrum(string preset)
        {
            AnalysisReport report = AudioAnalyzer.Analyze(RenderPreset(preset), SampleRate, new AnalysisSettings(1000, AudibleThresholdDb, 8192));
            Assert.True(report.RmsDbfs > AudibleThresholdDb, preset);
            Assert.Equal(0L, report.ClippedSampleCount);
            Assert.True(report.PeakDbfs < 0, preset);
            AnalysisWindow window = Assert.Single(report.Windows);
            if (SnesInstrumentCatalog.Get(preset).Category != "ドラム")
            {
                double semitoneRatio = Math.Pow(2, 1 / SemitonesPerOctave);
                Assert.InRange(window.DominantFrequencyHz, MiddleCFrequency / semitoneRatio, MiddleCFrequency * semitoneRatio);
            }
            if (preset == "kick")
            {
                Assert.True(window.SpectralCentroidHz < MaximumKickCentroid);
            }
            if (preset == "hat" || preset == "openhat")
            {
                Assert.True(window.SpectralCentroidHz > MinimumHatCentroid);
            }
        }

        /// <summary>BRR 往復後も strings の第七倍音は organ と明確に異なる。</summary>
        [Fact]
        public void StringsAndOrgan_HaveDifferentHarmonicRatios()
        {
            const int ComparedHarmonic = 7;
            const double MinimumRatioDifference = 0.03;
            float[] strings = SnesInstrumentBank.Build("strings").Samples.ToArray();
            float[] organ = SnesInstrumentBank.Build("organ").Samples.ToArray();
            double stringsRatio = HarmonicAmplitude(strings, ComparedHarmonic) / HarmonicAmplitude(strings, 1);
            double organRatio = HarmonicAmplitude(organ, ComparedHarmonic) / HarmonicAmplitude(organ, 1);
            Assert.True(stringsRatio - organRatio > MinimumRatioDifference);
        }

        /// <summary>プリセットを含む発音・再発音・エコー・分割 Render が確保せず一括出力と一致する。</summary>
        [Theory]
        [InlineData(SnesBankKind.Orchestral)]
        [InlineData(SnesBankKind.Band)]
        [InlineData(SnesBankKind.Chip)]
        public void Render_AllocatesZeroAndIsChunkInvariant(SnesBankKind bank)
        {
            const int NoteDuration = 48;
            const int SecondNoteTick = 60;
            const int SongLength = 120;
            const int ChunkLength = 514;
            Song song = SongFactory.Create(ChipKind.Snes, lengthTicks: SongLength, bank: bank);
            song.SnesEcho = new SnesEchoSettings { DelayMilliseconds = 32, Feedback = 0.2, Volume = 0.3, FirCoefficients = SnesEchoFirPresets.LowPass };
            foreach (Track track in song.Tracks)
            {
                int instrumentId = track.DefaultInstrumentId!.Value;
                track.Notes.Add(new Note { MidiNote = MiddleC, DurationTicks = NoteDuration, InstrumentId = instrumentId });
                track.Notes.Add(new Note { Tick = SecondNoteTick, MidiNote = MiddleC, DurationTicks = NoteDuration, InstrumentId = instrumentId });
            }
            RenderSettings settings = new RenderSettings(SampleRate, 1, 0);
            SongRenderer renderer = new SongRenderer(song, settings);
            float[] expected = renderer.RenderAll();
            float[] actual = new float[expected.Length];
            renderer.Reset();
            long before = GC.GetAllocatedBytesForCurrentThread();
            int position = 0;
            while (position < actual.Length)
            {
                int count = Math.Min(ChunkLength, actual.Length - position);
                position += renderer.Render(actual.AsSpan(position, count)) * StereoChannels;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
            Assert.Equal(expected, actual);
        }

        private static float[] RenderPreset(string preset)
        {
            SnesSampleInstrument instrument = new SnesSampleInstrument { Preset = preset };
            SnesVoiceSynthesizer voice = new SnesVoiceSynthesizer(SampleRate);
            voice.NoteOn(MiddleC, MaximumVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
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

        private static int ExpectedDrumLength(string preset)
        {
            const int MillisecondsPerSecond = 1000;
            int milliseconds = preset switch
            {
                "kick" => 300, "snare" => 150, "hat" => 40,
                "openhat" => 200, "tom" => 200, "crash" => 800,
                _ => throw new ArgumentException("ドラム名が不正です。", nameof(preset))
            };
            return SampleRate * milliseconds / MillisecondsPerSecond;
        }

        private static double HarmonicAmplitude(float[] samples, int harmonic)
        {
            double real = 0;
            double imaginary = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                double phase = 2 * Math.PI * harmonic * index / samples.Length;
                real += samples[index] * Math.Cos(phase);
                imaginary += samples[index] * Math.Sin(phase);
            }
            return Math.Sqrt(real * real + imaginary * imaginary);
        }
    }
}
