using System;
using System.IO;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Xunit;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>ソング複製、ソロ、合成警告、入力エラーの共有境界を検証する。</summary>
    public sealed class AudioAnalysisSourceTests
    {
        /// <summary>ミュート済みの選択トラックも鳴らし、元の全データは維持する。</summary>
        [Fact]
        public void AnalyzeSong_SolosSnapshotAndKeepsOriginal()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 48);
            song.Tracks[0].Notes.Add(new Note { MidiNote = 69 });
            song.Tracks[0].Muted = true;
            song.Tracks[1].Notes.Add(new Note { MidiNote = 48 });
            string before = SongSerializer.Serialize(song);
            AnalysisReport report = AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings(), track: 0, loops: 2);
            Assert.Equal(0.8, report.DurationSeconds, 8);
            Assert.All(report.Windows, window => Assert.Equal("A4", window.NearestNoteName));
            Assert.Equal(before, SongSerializer.Serialize(song));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings(), track: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings(), track: 5));
        }

        /// <summary>合成側の警告を解析レポートと共通のテキスト警告節に含める。</summary>
        [Fact]
        public void AnalyzeSong_IncludesSynthesisWarnings()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 48);
            song.Instruments.Add(new NesTriangleInstrument { Id = 2 });
            song.Tracks[2].Notes.Add(new Note { MidiNote = 60, Volume = 7, InstrumentId = 2 });
            AnalysisReport report = AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings());
            RenderWarning warning = Assert.Single(report.RenderWarnings);
            Assert.Equal(RenderWarningKind.TriangleVolumeIgnored, warning.Kind);
            Assert.Equal(2, warning.TrackIndex);
            Assert.Equal(0, warning.Tick);
            Assert.Contains("合成 TriangleVolumeIgnored", AnalysisTextRenderer.Render(report));
        }

        /// <summary>波形形式の破損は引数エラー、存在しないパスは I/O エラーになる。</summary>
        [Fact]
        public void AnalyzeWav_ClassifiesMalformedAndMissingFiles()
        {
            string path = Path.Combine(Path.GetTempPath(), "arpeggio-invalid-wave-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(path, "invalid");
                Assert.Throws<ArgumentException>(() => AudioAnalysisSource.AnalyzeWav(path, new AnalysisSettings()));
                File.Delete(path);
                Assert.Throws<FileNotFoundException>(() => AudioAnalysisSource.AnalyzeWav(path, new AnalysisSettings()));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
