using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;

namespace Arpeggio.Core.Analysis
{
    /// <summary>CLI / MCP 共通の入力読み込みと、元ソングを変更しない解析の入口。</summary>
    public static class AudioAnalysisSource
    {
        private const int SongSampleRate = 44100;

        /// <summary>ソングを複製して必要ならソロ化し、末尾余白なしで合成警告も返す。</summary>
        public static AnalysisReport AnalyzeSong(Song song, AnalysisSettings settings, int? track = null, int loops = 1)
        {
            AudioAnalyzer.Validate(0, SongSampleRate, settings);
            Song snapshot = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            if (track.HasValue)
            {
                if (track.Value < 0 || track.Value >= snapshot.Tracks.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(track), "トラック番号が範囲外です。");
                }
                for (int index = 0; index < snapshot.Tracks.Count; index++)
                {
                    snapshot.Tracks[index].Muted = index != track.Value;
                }
            }
            SongRenderer renderer = new SongRenderer(snapshot, new RenderSettings(SongSampleRate, loops, 0));
            float[] samples = renderer.RenderAll();
            AnalysisReport report = AudioAnalyzer.Analyze(samples, SongSampleRate, settings);
            report.RenderWarnings = Array.AsReadOnly(renderer.Report.Warnings.ToArray());
            report.DroppedRenderWarningCount = renderer.Report.DroppedWarningCount;
            return report;
        }

        /// <summary>WAV を読み込んで解析する。不正な音声形式は引数エラーとして通知する。</summary>
        public static AnalysisReport AnalyzeWav(string path, AnalysisSettings settings)
        {
            AudioAnalyzer.Validate(0, SongSampleRate, settings);
            try
            {
                float[] samples = WavReader.Read(path, out int sampleRate);
                return AudioAnalyzer.Analyze(samples, sampleRate, settings);
            }
            catch (InvalidDataException exception)
            {
                throw new ArgumentException(exception.Message, nameof(path), exception);
            }
        }
    }
}
