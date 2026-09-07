using System;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>既知の入力波形で音量・周波数・窓境界・警告を検証する。</summary>
    public sealed class AudioAnalyzerTests
    {
        private const int SampleRate = 44100;
        private const int StereoChannels = 2;
        private const double ReferenceFrequency = 440;
        private const double SineAmplitude = 0.5;

        /// <summary>440 Hz 正弦波の音名・音量・周波数・帯域を解析できる。</summary>
        [Theory]
        [InlineData(22050)]
        [InlineData(44100)]
        [InlineData(48000)]
        public void Analyze_SineReportsA4AndKnownLevels(int sampleRate)
        {
            float[] samples = CreateSine(sampleRate, sampleRate, ReferenceFrequency);
            AnalysisReport report = AudioAnalyzer.Analyze(samples, sampleRate, new AnalysisSettings());
            Assert.Equal(1, report.DurationSeconds);
            Assert.InRange(report.RmsDbfs, -9.04, -9.02);
            Assert.InRange(report.PeakDbfs, -6.04, -6.01);
            Assert.Equal(0, report.LeftRightBalanceDb);
            Assert.Equal(0, report.SilenceRatio);
            Assert.Equal(0, report.ClippedSampleCount);
            Assert.Empty(report.Warnings);
            Assert.Equal(10, report.Windows.Count);
            Assert.All(report.Windows, window =>
            {
                Assert.Equal("A4", window.NearestNoteName);
                Assert.InRange(window.DominantFrequencyHz, 435, 445);
                Assert.InRange(window.SpectralCentroidHz, 425, 455);
            });
            Assert.InRange(report.BandEnergy.MidRatio, 0.98, 1);
            Assert.Equal(1, report.BandEnergy.LowRatio + report.BandEnergy.MidRatio + report.BandEnergy.HighRatio, 10);
        }

        /// <summary>逆相ステレオでも周波数と帯域が消えない。</summary>
        [Fact]
        public void Analyze_AntiPhaseStereoPreservesPower()
        {
            float[] samples = CreateSine(SampleRate, SampleRate, ReferenceFrequency);
            for (int index = 1; index < samples.Length; index += StereoChannels)
            {
                samples[index] = -samples[index];
            }
            AnalysisReport report = AudioAnalyzer.Analyze(samples, SampleRate, new AnalysisSettings());
            Assert.All(report.Windows, window => Assert.Equal("A4", window.NearestNoteName));
            Assert.InRange(report.BandEnergy.MidRatio, 0.98, 1);
            Assert.Empty(report.Warnings);
        }

        /// <summary>完全無音でも全数値が JSON に直列化でき、三種の無音警告が出る。</summary>
        [Fact]
        public void Analyze_SilenceHasFiniteValuesAndWarnings()
        {
            AnalysisReport report = AudioAnalyzer.Analyze(new float[SampleRate * StereoChannels], SampleRate, new AnalysisSettings());
            Assert.Equal(DecibelScale.MinimumDecibels, report.RmsDbfs);
            Assert.Equal(DecibelScale.MinimumDecibels, report.PeakDbfs);
            Assert.Equal(1, report.SilenceRatio);
            Assert.Equal(0, report.LeftRightBalanceDb);
            Assert.Equal(new AnalysisBandEnergy(), report.BandEnergy);
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.TooQuiet);
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.LeadingSilence);
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.TrailingSilence);
            Assert.All(report.Windows, window =>
            {
                Assert.Equal(0, window.DominantFrequencyHz);
                Assert.Null(window.NearestNoteName);
                Assert.Equal(0, window.SpectralCentroidHz);
            });
            using JsonDocument document = JsonDocument.Parse(SessionOutput.Serialize(report));
            Assert.Equal(-160, document.RootElement.GetProperty("rmsDbfs").GetDouble());
        }

        /// <summary>クリップ数は左右個別で、絶対値 1 の境界も含む。</summary>
        [Fact]
        public void Analyze_CountsClipsAndKeepsInput()
        {
            float[] samples = { 1, -1, 1.25f, -1.5f, 0.9f, -0.9f };
            float[] before = (float[])samples.Clone();
            AnalysisReport report = AudioAnalyzer.Analyze(samples, SampleRate, new AnalysisSettings());
            Assert.Equal(4, report.ClippedSampleCount);
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.Clipping);
            Assert.True(report.PeakDbfs > 0);
            Assert.Equal(before, samples);
        }

        /// <summary>端数窓を一窓と数え、全体 RMS は窓平均ではなくサンプル数で重み付けする。</summary>
        [Fact]
        public void Analyze_PartialWindowUsesActualFrameCount()
        {
            const int Rate = 1000;
            const int AudibleFrames = 100;
            const int TotalFrames = 101;
            float[] samples = new float[TotalFrames * StereoChannels];
            Array.Fill(samples, 0.5f, 0, AudibleFrames * StereoChannels);
            AnalysisReport report = AudioAnalyzer.Analyze(samples, Rate, new AnalysisSettings());
            Assert.Equal(2, report.Windows.Count);
            Assert.Equal(0.1, report.Windows[1].StartSeconds, 10);
            Assert.Equal(0.5, report.SilenceRatio);
            double expected = 20 * Math.Log10(0.5 * Math.Sqrt((double)AudibleFrames / TotalFrames));
            Assert.Equal(expected, report.RmsDbfs, 8);
        }

        /// <summary>帯域ごとに十分離れた正弦波を正しい帯域へ分類する。</summary>
        [Theory]
        [InlineData(100, 0)]
        [InlineData(1000, 1)]
        [InlineData(5000, 2)]
        public void Analyze_SeparatesFrequencyBands(double frequency, int expectedBand)
        {
            AnalysisReport report = AudioAnalyzer.Analyze(CreateSine(SampleRate, SampleRate, frequency), SampleRate, new AnalysisSettings());
            double[] ratios = { report.BandEnergy.LowRatio, report.BandEnergy.MidRatio, report.BandEnergy.HighRatio };
            Assert.InRange(ratios[expectedBand], 0.95, 1);
        }

        /// <summary>先頭と末尾の警告は表示窓長に依存しない。</summary>
        [Theory]
        [InlineData(17)]
        [InlineData(100)]
        [InlineData(700)]
        public void Analyze_EdgeSilenceIgnoresDisplayWindow(int windowMilliseconds)
        {
            const int Rate = 1000;
            const int FirstSoundFrame = 50;
            const int LastSoundFrame = 200;
            const int TotalFrames = 1200;
            float[] samples = new float[TotalFrames * StereoChannels];
            Array.Fill(samples, 0.5f, FirstSoundFrame * StereoChannels, (LastSoundFrame - FirstSoundFrame) * StereoChannels);
            AnalysisReport report = AudioAnalyzer.Analyze(samples, Rate, new AnalysisSettings(WindowMilliseconds: windowMilliseconds));
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.LeadingSilence);
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.TrailingSilence);
            float[] shorter = samples.Take(samples.Length - StereoChannels).ToArray();
            AnalysisReport shortened = AudioAnalyzer.Analyze(shorter, Rate, new AnalysisSettings(WindowMilliseconds: windowMilliseconds));
            Assert.DoesNotContain(shortened.Warnings, warning => warning.Kind == AnalysisWarningKind.TrailingSilence);
        }

        /// <summary>左右差は左−右の符号を持ち、片側無音でも有限値になる。</summary>
        [Theory]
        [InlineData(0.5f, 0.25f, true)]
        [InlineData(0.25f, 0.5f, false)]
        [InlineData(0.5f, 0, true)]
        public void Analyze_BalanceHasDirection(float left, float right, bool leftIsLouder)
        {
            float[] samples = { left, right, left, right };
            AnalysisReport report = AudioAnalyzer.Analyze(samples, SampleRate, new AnalysisSettings());
            Assert.Equal(leftIsLouder, report.LeftRightBalanceDb > 0);
            Assert.True(double.IsFinite(report.LeftRightBalanceDb));
            Assert.Contains(report.Warnings, warning => warning.Kind == AnalysisWarningKind.StereoImbalance);
        }

        /// <summary>空入力をゼロ長の解析結果として扱う。</summary>
        [Fact]
        public void Analyze_EmptyInputIsWellDefined()
        {
            AnalysisReport report = AudioAnalyzer.Analyze(Array.Empty<float>(), SampleRate, new AnalysisSettings());
            Assert.Equal(0, report.DurationSeconds);
            Assert.Equal(0, report.SilenceRatio);
            Assert.Empty(report.Windows);
            Assert.Equal(AnalysisWarningKind.TooQuiet, Assert.Single(report.Warnings).Kind);
            Assert.Contains("警告:", AnalysisTextRenderer.Render(report));
        }

        /// <summary>ゼロ初期化設定・不正窓・非有限サンプル・奇数長を拒否する。</summary>
        [Fact]
        public void Analyze_RejectsInvalidInput()
        {
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(Array.Empty<float>(), SampleRate, default));
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(Array.Empty<float>(), SampleRate, new AnalysisSettings(WindowMilliseconds: 0)));
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(Array.Empty<float>(), SampleRate, new AnalysisSettings(FftSize: 1000)));
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(Array.Empty<float>(), SampleRate, new AnalysisSettings(SilenceThresholdDb: double.NaN)));
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(new[] { float.NaN, 0 }, SampleRate, new AnalysisSettings()));
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(new[] { float.PositiveInfinity, 0 }, SampleRate, new AnalysisSettings()));
            Assert.Throws<ArgumentException>(() => AudioAnalyzer.Analyze(new float[1], SampleRate, new AnalysisSettings()));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioAnalyzer.Analyze(Array.Empty<float>(), 0, new AnalysisSettings()));
        }

        /// <summary>表示は 40 窓に限定し、JSON は全窓を保持する。</summary>
        [Fact]
        public void RenderText_LimitsWindowsWithoutTruncatingJson()
        {
            const int Frames = 4100;
            AnalysisReport report = AudioAnalyzer.Analyze(new float[Frames * StereoChannels], 1000, new AnalysisSettings());
            string text = AnalysisTextRenderer.Render(report);
            Assert.Contains("3.9000 |", text);
            Assert.DoesNotContain("4.0000 |", text);
            Assert.Contains("残り 1 窓", text);
            using JsonDocument document = JsonDocument.Parse(SessionOutput.Serialize(report));
            Assert.Equal(41, document.RootElement.GetProperty("windows").GetArrayLength());
        }

        /// <summary>大きな直流成分を持つ音でも、音量を保ったまま周期成分を検出する。</summary>
        [Fact]
        public void Analyze_DcOffsetDoesNotMaskTone()
        {
            const float ToneScale = 0.04f;
            const float Offset = 0.7f;
            float[] samples = CreateSine(SampleRate, SampleRate, ReferenceFrequency);
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = samples[index] * ToneScale + Offset;
            }
            AnalysisReport report = AudioAnalyzer.Analyze(samples, SampleRate, new AnalysisSettings());
            Assert.True(report.RmsDbfs > -4);
            Assert.All(report.Windows, window => Assert.Equal("A4", window.NearestNoteName));
            Assert.InRange(report.BandEnergy.MidRatio, 0.98, 1);
        }

        /// <summary>表示窓の先頭 FFT 区間だけが無音でも、後半の発音を周波数解析に含める。</summary>
        [Fact]
        public void Analyze_UsesTheEntireDisplayWindow()
        {
            const int FramesPerWindow = 4410;
            const int SilentFrames = 2205;
            float[] samples = CreateSine(FramesPerWindow, SampleRate, ReferenceFrequency);
            Array.Clear(samples, 0, SilentFrames * StereoChannels);
            AnalysisReport report = AudioAnalyzer.Analyze(samples, SampleRate, new AnalysisSettings());
            Assert.Equal("A4", Assert.Single(report.Windows).NearestNoteName);
            Assert.Equal(0, report.SilenceRatio);
        }

        private static float[] CreateSine(int frames, int sampleRate, double frequency)
        {
            float[] samples = new float[frames * StereoChannels];
            for (int frame = 0; frame < frames; frame++)
            {
                float value = (float)(SineAmplitude * Math.Sin(2 * Math.PI * frequency * frame / sampleRate));
                samples[frame * StereoChannels] = value;
                samples[frame * StereoChannels + 1] = value;
            }
            return samples;
        }
    }
}
