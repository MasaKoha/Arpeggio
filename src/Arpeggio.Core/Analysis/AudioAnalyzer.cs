using System;
using System.Collections.Generic;

namespace Arpeggio.Core.Analysis
{
    /// <summary>インターリーブ済みステレオ音声を AI が比較できる数値に変換する。</summary>
    public static class AudioAnalyzer
    {
        private const int StereoChannels = 2;
        private const int MillisecondsPerSecond = 1000;
        private const int EdgeWindowMilliseconds = 50;
        private const double MinimumUsefulLevelDb = -30;
        private const double MaximumBalanceDifferenceDb = 6;
        private const double TrailingSilenceWarningSeconds = 1;

        /// <summary>音量・時間変化・周波数・帯域・警告を解析する。入力波形は変更しない。</summary>
        public static AnalysisReport Analyze(ReadOnlySpan<float> interleavedStereo, int sampleRate, AnalysisSettings settings)
        {
            Validate(interleavedStereo.Length, sampleRate, settings);
            SignalStatistics overall = SignalStatistics.Measure(interleavedStereo);
            int frames = interleavedStereo.Length / StereoChannels;
            int windowFrames = GetWindowFrames(sampleRate, settings.WindowMilliseconds);
            int windowCount = frames / windowFrames + (frames % windowFrames == 0 ? 0 : 1);
            AnalysisWindow[] windows = new AnalysisWindow[windowCount];
            SpectrumAnalysis spectrum = new SpectrumAnalysis(settings.FftSize, sampleRate);
            int silentWindows = 0;
            for (int index = 0; index < windowCount; index++)
            {
                int start = index * windowFrames;
                int count = Math.Min(windowFrames, frames - start);
                ReadOnlySpan<float> samples = interleavedStereo.Slice(start * StereoChannels, count * StereoChannels);
                windows[index] = spectrum.Measure(samples, (double)start / sampleRate, SignalStatistics.Measure(samples));
                if (windows[index].RmsDbfs < settings.SilenceThresholdDb)
                {
                    silentWindows++;
                }
            }
            AnalysisReport report = new AnalysisReport
            {
                DurationSeconds = (double)frames / sampleRate,
                RmsDbfs = DecibelScale.ToDecibels(overall.Rms),
                PeakDbfs = DecibelScale.ToDecibels(overall.Peak),
                ClippedSampleCount = overall.ClippedSamples,
                SilenceRatio = windowCount == 0 ? 0 : (double)silentWindows / windowCount,
                LeftRightBalanceDb = DecibelScale.ToDecibels(Math.Sqrt(overall.LeftPower)) - DecibelScale.ToDecibels(Math.Sqrt(overall.RightPower)),
                Windows = Array.AsReadOnly(windows),
                BandEnergy = spectrum.GetBandEnergy(),
                SampleRate = sampleRate,
                Settings = settings
            };
            report.Warnings = FindWarnings(interleavedStereo, report);
            return report;
        }

        internal static void Validate(int sampleCount, int sampleRate, AnalysisSettings settings)
        {
            if (sampleCount % StereoChannels != 0)
            {
                throw new ArgumentException("ステレオ音声は左右一組で指定してください。", "interleavedStereo");
            }
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            bool validThreshold = double.IsFinite(settings.SilenceThresholdDb)
                && settings.SilenceThresholdDb > DecibelScale.MinimumDecibels && settings.SilenceThresholdDb <= 0;
            bool validTransformSize = settings.FftSize >= 2 && (settings.FftSize & (settings.FftSize - 1)) == 0;
            if (settings.WindowMilliseconds <= 0 || !validThreshold || !validTransformSize)
            {
                throw new ArgumentException("窓長は正整数、閾値は -160 より大きく 0 以下、FFT 点数は 2 以上の 2 の累乗です。", nameof(settings));
            }
        }

        private static int GetWindowFrames(int sampleRate, int milliseconds)
        {
            double frames = (double)sampleRate * milliseconds / MillisecondsPerSecond;
            return checked((int)Math.Max(1, Math.Round(frames, MidpointRounding.AwayFromZero)));
        }

        private static IReadOnlyList<AnalysisWarning> FindWarnings(ReadOnlySpan<float> samples, AnalysisReport report)
        {
            List<AnalysisWarning> warnings = new List<AnalysisWarning>();
            if (report.ClippedSampleCount > 0)
            {
                warnings.Add(new AnalysisWarning { Kind = AnalysisWarningKind.Clipping, Message = $"クリップしたサンプルが {report.ClippedSampleCount} 個あります。" });
            }
            if (report.RmsDbfs < MinimumUsefulLevelDb)
            {
                warnings.Add(new AnalysisWarning { Kind = AnalysisWarningKind.TooQuiet, Message = "全体 RMS が -30 dBFS 未満で小さすぎます。" });
            }
            int edgeFrames = GetWindowFrames(report.SampleRate, EdgeWindowMilliseconds);
            int firstSamples = Math.Min(samples.Length / StereoChannels, edgeFrames) * StereoChannels;
            if (firstSamples > 0 && IsSilent(samples.Slice(0, firstSamples), report.Settings.SilenceThresholdDb))
            {
                warnings.Add(new AnalysisWarning { Kind = AnalysisWarningKind.LeadingSilence, Message = "先頭 50 ms（短い音声では全区間）が無音です。" });
            }
            if (GetTrailingSilenceSeconds(samples, report, edgeFrames) >= TrailingSilenceWarningSeconds)
            {
                warnings.Add(new AnalysisWarning { Kind = AnalysisWarningKind.TrailingSilence, Message = "末尾に 1 秒以上の連続無音があります。" });
            }
            if (Math.Abs(report.LeftRightBalanceDb) > MaximumBalanceDifferenceDb)
            {
                warnings.Add(new AnalysisWarning { Kind = AnalysisWarningKind.StereoImbalance, Message = "左右の RMS 差が 6 dB を超えています。" });
            }
            return warnings.AsReadOnly();
        }

        private static double GetTrailingSilenceSeconds(ReadOnlySpan<float> samples, AnalysisReport report, int edgeFrames)
        {
            int end = samples.Length / StereoChannels;
            int silentFrames = 0;
            while (end > 0)
            {
                int count = Math.Min(end, edgeFrames);
                int start = end - count;
                if (!IsSilent(samples.Slice(start * StereoChannels, count * StereoChannels), report.Settings.SilenceThresholdDb))
                {
                    break;
                }
                silentFrames += count;
                end = start;
            }
            return (double)silentFrames / report.SampleRate;
        }

        private static bool IsSilent(ReadOnlySpan<float> samples, double threshold)
        {
            return DecibelScale.ToDecibels(SignalStatistics.Measure(samples).Rms) < threshold;
        }
    }
}
