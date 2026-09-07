using System;
using System.Collections.Generic;
using Arpeggio.Core.Render;

namespace Arpeggio.Core.Analysis
{
    /// <summary>音声全体・時系列・帯域・合成と音響の警告を保持する。</summary>
    public sealed class AnalysisReport
    {
        /// <summary>音声の長さ（秒）。</summary>
        public double DurationSeconds { get; init; }
        /// <summary>全体 RMS（dBFS）。</summary>
        public double RmsDbfs { get; init; }
        /// <summary>全体ピーク（dBFS）。</summary>
        public double PeakDbfs { get; init; }
        /// <summary>絶対値 1 以上の左右個別サンプル数。</summary>
        public long ClippedSampleCount { get; init; }
        /// <summary>閾値未満の窓数 / 全窓数。</summary>
        public double SilenceRatio { get; init; }
        /// <summary>左 RMS − 右 RMS（dB）。正が左寄り。</summary>
        public double LeftRightBalanceDb { get; init; }
        /// <summary>解析時の設定。</summary>
        public AnalysisSettings Settings { get; init; }
        /// <summary>入力のサンプルレート（Hz）。</summary>
        public int SampleRate { get; init; }
        /// <summary>省略のない時系列。</summary>
        public IReadOnlyList<AnalysisWindow> Windows { get; init; } = Array.Empty<AnalysisWindow>();
        /// <summary>全体の帯域パワー比率。無音ではすべて 0。</summary>
        public AnalysisBandEnergy BandEnergy { get; init; }
        /// <summary>音響指標の警告。</summary>
        public IReadOnlyList<AnalysisWarning> Warnings { get; internal set; } = Array.Empty<AnalysisWarning>();
        /// <summary>ソングを合成した際の補正警告。WAV 入力では空。</summary>
        public IReadOnlyList<RenderWarning> RenderWarnings { get; internal set; } = Array.Empty<RenderWarning>();
        /// <summary>合成側の保持上限を超えた警告数。</summary>
        public long DroppedRenderWarningCount { get; internal set; }
    }
}
