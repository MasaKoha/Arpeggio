using System;
using System.Numerics;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Analysis
{
    /// <summary>左右のパワーを合算し、呼び出し全体で変換作業領域を再利用する。</summary>
    internal sealed class SpectrumAnalysis
    {
        private const int StereoChannels = 2;
        private const double LowBandBoundaryHz = 200;
        private const double HighBandBoundaryHz = 2000;
        private const double ReferenceFrequencyHz = 440;
        private const int ReferenceMidiNote = 69;
        private const int SemitonesPerOctave = 12;
        private const int MaximumMidiNote = 127;
        private readonly Complex[] spectrum;
        private readonly double[] windowPower;
        private readonly double[] totalPower;
        private readonly double[] weights;
        private readonly double frequencyStep;

        internal SpectrumAnalysis(int transformSize, int sampleRate)
        {
            spectrum = new Complex[transformSize];
            windowPower = new double[transformSize / 2 + 1];
            totalPower = new double[windowPower.Length];
            weights = new double[transformSize];
            frequencyStep = (double)sampleRate / transformSize;
        }

        internal AnalysisWindow Measure(ReadOnlySpan<float> samples, double startSeconds, SignalStatistics statistics)
        {
            Array.Clear(windowPower, 0, windowPower.Length);
            int frameCount = samples.Length / StereoChannels;
            for (int start = 0; start < frameCount; start += spectrum.Length)
            {
                int count = Math.Min(spectrum.Length, frameCount - start);
                double weightPower = PrepareWeights(count);
                ReadOnlySpan<float> block = samples.Slice(start * StereoChannels, count * StereoChannels);
                AddChannel(block, 0, weightPower);
                AddChannel(block, 1, weightPower);
            }
            double powerSum = 0;
            double frequencySum = 0;
            int dominant = 0;
            for (int index = 1; index < windowPower.Length; index++)
            {
                totalPower[index] += windowPower[index];
                powerSum += windowPower[index];
                frequencySum += windowPower[index] * index * frequencyStep;
                if (index > 0 && (dominant == 0 || windowPower[index] > windowPower[dominant]))
                {
                    dominant = index;
                }
            }
            double frequency = dominant == 0 || windowPower[dominant] == 0 ? 0 : InterpolatePeak(dominant) * frequencyStep;
            return new AnalysisWindow
            {
                StartSeconds = startSeconds,
                RmsDbfs = DecibelScale.ToDecibels(statistics.Rms),
                PeakDbfs = DecibelScale.ToDecibels(statistics.Peak),
                DominantFrequencyHz = frequency,
                NearestNoteName = FindNoteName(frequency),
                SpectralCentroidHz = powerSum == 0 ? 0 : frequencySum / powerSum
            };
        }

        internal AnalysisBandEnergy GetBandEnergy()
        {
            double low = 0;
            double middle = 0;
            double high = 0;
            for (int index = 0; index < totalPower.Length; index++)
            {
                double frequency = index * frequencyStep;
                if (frequency < LowBandBoundaryHz)
                {
                    low += totalPower[index];
                }
                else if (frequency <= HighBandBoundaryHz)
                {
                    middle += totalPower[index];
                }
                else
                {
                    high += totalPower[index];
                }
            }
            double total = low + middle + high;
            return total == 0 ? new AnalysisBandEnergy() : new AnalysisBandEnergy
            {
                LowRatio = low / total,
                MidRatio = middle / total,
                HighRatio = high / total
            };
        }

        private double PrepareWeights(int count)
        {
            double sum = 0;
            for (int index = 0; index < count; index++)
            {
                double weight = count <= 2 ? 1 : 0.5 * (1 - Math.Cos(2 * Math.PI * index / (count - 1)));
                weights[index] = weight;
                sum += weight * weight;
            }
            return sum;
        }

        private void AddChannel(ReadOnlySpan<float> samples, int channel, double weightPower)
        {
            int count = samples.Length / StereoChannels;
            Array.Clear(spectrum, 0, spectrum.Length);
            double mean = 0;
            for (int index = 0; index < count; index++)
            {
                mean += samples[index * StereoChannels + channel];
            }
            mean /= count;
            // 非対称 Pulse の直流成分が Hann 窓で隣のビンに漏れ、音程と誤認されるのを防ぐ。
            for (int index = 0; index < count; index++)
            {
                spectrum[index] = new Complex((samples[index * StereoChannels + channel] - mean) * weights[index], 0);
            }
            FastFourierTransform.Transform(spectrum);
            // 端数ブロックが通常ブロックと同じ重みにならないよう、実フレーム数を反映する。
            double scale = count / (weightPower * spectrum.Length);
            for (int index = 0; index < windowPower.Length; index++)
            {
                Complex value = spectrum[index];
                double oneSidedFactor = index == 0 || index == spectrum.Length / 2 ? 1 : 2;
                windowPower[index] += (value.Real * value.Real + value.Imaginary * value.Imaginary) * scale * oneSidedFactor;
            }
        }

        private double InterpolatePeak(int index)
        {
            if (index <= 1 || index >= windowPower.Length - 1 || windowPower[index - 1] == 0 || windowPower[index + 1] == 0)
            {
                return index;
            }
            double before = Math.Log(windowPower[index - 1]);
            double center = Math.Log(windowPower[index]);
            double after = Math.Log(windowPower[index + 1]);
            double curvature = before - 2 * center + after;
            double correction = curvature == 0 ? 0 : 0.5 * (before - after) / curvature;
            return index + Math.Clamp(correction, -0.5, 0.5);
        }

        private static string? FindNoteName(double frequency)
        {
            if (frequency <= 0)
            {
                return null;
            }
            int note = (int)Math.Round(ReferenceMidiNote + SemitonesPerOctave * Math.Log2(frequency / ReferenceFrequencyHz));
            return note < 0 || note > MaximumMidiNote ? null : NoteName.Format(note);
        }
    }
}
