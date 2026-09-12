using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arpeggio.Core.Analysis;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Presets
{
    /// <summary>全プリセット・全チップの生成、保存、合成、WAV 再解析を検証する。</summary>
    public sealed class SfxPresetFactoryTests
    {
        private const int SampleRate = 44100;

        /// <summary>八種の音作りに要求する長さの範囲と三チップの組み合わせ。</summary>
        public static IEnumerable<object[]> Cases()
        {
            (SfxPresetKind Kind, double Minimum, double Maximum)[] presets =
            {
                (SfxPresetKind.Jump, 0.14, 0.17),
                (SfxPresetKind.Coin, 0.10, 0.20),
                (SfxPresetKind.Hit, 0.10, 0.20),
                (SfxPresetKind.Explosion, 0.30, 0.50),
                (SfxPresetKind.PowerUp, 0.30, 0.50),
                (SfxPresetKind.Laser, 0.10, 0.20),
                (SfxPresetKind.Blip, 0.03, 0.05),
                (SfxPresetKind.Select, 0.08, 0.12)
            };
            ChipKind[] chips = { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes };
            foreach (ChipKind chip in chips)
            {
                foreach ((SfxPresetKind kind, double minimum, double maximum) in presets)
                {
                    yield return new object[] { chip, kind, minimum, maximum };
                }
            }
        }

        /// <summary>保存形式を往復した効果音の WAV が非無音・想定長・非クリップになる。</summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void Create_AllPresetsRenderAndAnalyze(ChipKind chip, SfxPresetKind kind, double minimum, double maximum)
        {
            Song song = SfxPresetFactory.Create(chip, kind);
            SongValidator.Validate(song);
            Assert.Equal(chip, song.Chip);
            Assert.Equal(150, song.TempoBpm);
            Assert.Equal(Song.CurrentVersion, song.Version);
            Assert.Equal(0, song.LoopStartTick);
            int noteEnd = song.Tracks.SelectMany(track => track.Notes).Max(note => note.Tick + note.DurationTicks);
            Assert.Equal(noteEnd, song.LengthTicks);
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            SongRenderer renderer = new SongRenderer(restored, new RenderSettings(SampleRate, 1, 0));
            float[] samples = renderer.RenderAll();
            using MemoryStream stream = new MemoryStream();
            WavWriter.Write(stream, samples, SampleRate);
            stream.Position = 0;
            float[] wave = WavReader.Read(stream, out int sampleRate);
            AnalysisReport report = AudioAnalyzer.Analyze(wave, sampleRate, new AnalysisSettings());
            Assert.InRange(report.DurationSeconds, minimum, maximum);
            Assert.True(report.RmsDbfs > -60);
            Assert.True(report.PeakDbfs < 0);
            Assert.Equal(0, report.ClippedSampleCount);
            Assert.DoesNotContain(report.Warnings, warning => warning.Kind == AnalysisWarningKind.Clipping);
            Assert.DoesNotContain(report.Warnings, warning => warning.Kind == AnalysisWarningKind.LeadingSilence);
            Assert.Empty(renderer.Report.Warnings);
        }

        /// <summary>上昇と下降の方向が解析時系列にも現れる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, SfxPresetKind.Jump, true)]
        [InlineData(ChipKind.GameBoy, SfxPresetKind.Jump, true)]
        [InlineData(ChipKind.Snes, SfxPresetKind.Jump, true)]
        [InlineData(ChipKind.Nes, SfxPresetKind.Laser, false)]
        [InlineData(ChipKind.GameBoy, SfxPresetKind.Laser, false)]
        [InlineData(ChipKind.Snes, SfxPresetKind.Laser, false)]
        public void Create_SlidesHaveExpectedDirection(ChipKind chip, SfxPresetKind kind, bool ascending)
        {
            AnalysisReport report = AudioAnalysisSource.AnalyzeSong(SfxPresetFactory.Create(chip, kind), new AnalysisSettings(WindowMilliseconds: 50));
            double first = report.Windows[0].DominantFrequencyHz;
            double last = report.Windows[report.Windows.Count - 1].DominantFrequencyHz;
            Assert.True(first > 0);
            Assert.True(last > 0);
            Assert.Equal(ascending, last > first);
        }

        /// <summary>Explosion の後半は前半より減衰し、ノイズに対応する音色を使う。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void Create_ExplosionDecays(ChipKind chip)
        {
            Song song = SfxPresetFactory.Create(chip, SfxPresetKind.Explosion);
            AnalysisReport report = AudioAnalysisSource.AnalyzeSong(song, new AnalysisSettings());
            Assert.True(report.Windows[report.Windows.Count - 1].RmsDbfs < report.Windows[0].RmsDbfs - 6);
            if (chip == ChipKind.Snes)
            {
                Assert.Contains(song.Instruments, instrument => instrument is SnesSampleInstrument sample && sample.Waveform == SnesWaveformKind.Noise);
            }
            else
            {
                Assert.Contains(song.Tracks, track => track.Channel == ChannelKind.Noise && track.Notes.Count > 0);
            }
        }

        /// <summary>カタログ名は往復でき、未指定・未知名・不正チップを拒否する。</summary>
        [Fact]
        public void Catalog_RoundTripsNamesAndRejectsUnknownValues()
        {
            Assert.Equal(8, SfxPresetCatalog.GetAll().Count);
            foreach (SfxPresetDescription description in SfxPresetCatalog.GetAll())
            {
                Assert.Equal(description.Kind, SfxPresetCatalog.Parse(description.Name.ToUpperInvariant()));
                Assert.False(string.IsNullOrWhiteSpace(description.Description));
            }
            Assert.Equal(SfxPresetKind.PowerUp, SfxPresetCatalog.Parse("power-up"));
            Assert.Throws<ArgumentException>(() => SfxPresetCatalog.Parse("None"));
            Assert.Throws<ArgumentException>(() => SfxPresetCatalog.Parse("1"));
            Assert.Throws<ArgumentOutOfRangeException>(() => SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.None));
            Assert.Throws<SongValidationException>(() => SfxPresetFactory.Create(ChipKind.None, SfxPresetKind.Jump));
        }

        /// <summary>生成結果の可変データは呼び出し間で共有しない。</summary>
        [Fact]
        public void Create_DoesNotShareMutableNotesOrInstruments()
        {
            Song first = SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.Coin);
            Song second = SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.Coin);
            string before = SongSerializer.Serialize(second);
            first.Tracks[0].Notes[0].MidiNote = 60;
            first.Instruments[0].Name = "changed";
            Assert.Equal(before, SongSerializer.Serialize(second));
        }
    }
}
