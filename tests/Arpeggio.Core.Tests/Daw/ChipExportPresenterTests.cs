using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Daw.Presenters.Export;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>チップ書き出しの候補・診断済み内容・strict・上書き保護を UI 非依存で検証する。</summary>
    public sealed class ChipExportPresenterTests
    {
        /// <summary>OS ピッカーと手入力の検証が同じチップ別候補を使う。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ".wav,.ogg,.nsf,.vgm")]
        [InlineData(ChipKind.GameBoy, ".wav,.ogg,.vgm")]
        [InlineData(ChipKind.Snes, ".wav,.ogg")]
        public void OffersOnlySupportedExtensions(ChipKind chip, string expected)
        {
            Assert.Equal(expected.Split(','), ExportFileTypes.GetExtensions(chip));
            foreach (string extension in expected.Split(','))
            {
                ExportFileTypes.Validate("song" + extension.ToUpperInvariant(), chip);
            }
            Assert.Equal(ConversionFormat.Nsf, ExportFileTypes.GetChipFormat("song.NSF"));
            Assert.Equal(ConversionFormat.Vgm, ExportFileTypes.GetChipFormat("song.VGM"));
            Assert.Equal(ConversionFormat.None, ExportFileTypes.GetChipFormat("song.WAV"));
            Assert.Equal(ConversionFormat.None, ExportFileTypes.GetChipFormat("song.OGG"));
            Assert.Throws<ArgumentException>(() => ExportFileTypes.Validate("song.txt", chip));
            Assert.Throws<ArgumentException>(() => ExportFileTypes.GetChipFormat("song.txt"));
        }

        /// <summary>対応しないチップ形式は手入力でも設定・保存へ進まない。</summary>
        [Theory]
        [InlineData(ChipKind.GameBoy, ".nsf")]
        [InlineData(ChipKind.Snes, ".nsf")]
        [InlineData(ChipKind.Snes, ".vgm")]
        [InlineData(ChipKind.Nes, ".txt")]
        public void RejectsIncompatibleTypedExtensions(ChipKind chip, string extension)
        {
            using var fixture = new DawPresenterFixture(chip);
            ExportPresenter presenter = fixture.Presenter.Export;
            Assert.Throws<ArgumentException>(() => presenter.SelectChipDestination(Path.ChangeExtension(fixture.Path, extension)));
            Assert.Null(presenter.ChipDestinationPath);
            Assert.Null(presenter.PreparedChipPlan);
            Assert.False(presenter.CanSaveChip);
        }

        /// <summary>設定段階から制限を示し、診断と保存の間の編集にかかわらず同じ plan の byte 列を保存する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ConversionFormat.Nsf, ".nsf")]
        [InlineData(ChipKind.Nes, ConversionFormat.Vgm, ".vgm")]
        [InlineData(ChipKind.GameBoy, ConversionFormat.Vgm, ".vgm")]
        public async Task SavesExactlyThePreparedPlan(ChipKind chip, ConversionFormat format, string extension)
        {
            using var fixture = new DawPresenterFixture(chip);
            fixture.Presenter.Transport.SetLength(48);
            fixture.Presenter.PianoRoll.Add(0, 69);
            int prepareCalls = 0;
            ChipExportPlan? writtenPlan = null;
            string? protectedSource = null;
            using var presenter = new ExportPresenter(fixture.Document, fixture.View, () => { },
                prepareChip: (song, options) =>
                {
                    prepareCalls++;
                    return ChipExportService.Prepare(song, options);
                },
                writeChip: (plan, path, overwrite, cancellationToken, sourcePath) =>
                {
                    writtenPlan = plan;
                    protectedSource = sourcePath;
                    return ChipExportService.Write(plan, path, overwrite, cancellationToken, sourcePath);
                });
            string output = Path.ChangeExtension(fixture.Path, extension);
            presenter.SelectChipDestination(output);
            Assert.Contains("通常の再生", presenter.ChipReportText);
            Assert.Contains("無限ループ", presenter.ChipReportText);
            Assert.Equal(format, presenter.SelectedChipFormat);
            Assert.False(presenter.CanSaveChip);
            string initialSong = SongSerializer.Serialize(fixture.Document.Song);
            int initialHistory = fixture.Document.Session.History.UndoCount;
            byte[] sourceBytes = File.ReadAllBytes(fixture.Path);
            await presenter.PrepareChipAsync(new ChipExportOptions
            {
                Format = format, Loops = 2, Author = "author", Copyright = format == ConversionFormat.Nsf ? "rights" : ""
            });
            ChipExportPlan plan = Assert.IsType<ChipExportPlan>(presenter.PreparedChipPlan);
            Assert.True(presenter.CanSaveChip);
            Assert.Contains("秒", presenter.ChipReportText);
            Assert.Contains("byte", presenter.ChipReportText);
            Assert.False(File.Exists(output));
            Assert.Equal(initialSong, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(initialHistory, fixture.Document.Session.History.UndoCount);
            using var expected = new MemoryStream();
            Assert.True(ChipExportService.Write(plan, expected));
            string report = SessionOutput.Serialize(plan.Report);
            fixture.Presenter.Transport.SetTempo(100);
            string editedSong = SongSerializer.Serialize(fixture.Document.Song);
            int editedHistory = fixture.Document.Session.History.UndoCount;
            await presenter.SaveChipAsync();
            Assert.Same(plan, writtenPlan);
            Assert.Equal(1, prepareCalls);
            Assert.Equal(fixture.Document.Path, protectedSource);
            Assert.Equal(expected.ToArray(), File.ReadAllBytes(output));
            Assert.Equal(report, SessionOutput.Serialize(plan.Report));
            Assert.Equal(output, presenter.LastExportedPath);
            Assert.Contains("書き出し完了", fixture.View.ExportStatus);
            Assert.False(presenter.CanSaveChip);
            Assert.Equal(editedSong, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(editedHistory, fixture.Document.Session.History.UndoCount);
            Assert.Equal(sourceBytes, File.ReadAllBytes(fixture.Path));
            Assert.Equal(0, fixture.Audio.CallbackCount);
        }

        /// <summary>警告は元位置付きで表示し、strict は全診断後に保存を拒否する。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf, ".nsf", false)]
        [InlineData(ConversionFormat.Nsf, ".nsf", true)]
        [InlineData(ConversionFormat.Vgm, ".vgm", false)]
        [InlineData(ConversionFormat.Vgm, ".vgm", true)]
        public async Task ShowsWarningsAndHonorsStrict(ConversionFormat format, string extension, bool strict)
        {
            using var fixture = new DawPresenterFixture();
            fixture.Presenter.Transport.SetLength(48);
            fixture.Presenter.PianoRoll.Add(0, 69);
            fixture.Document.Song.Tracks[0].Pan = 0.25;
            ExportPresenter presenter = fixture.Presenter.Export;
            string output = Path.ChangeExtension(fixture.Path, extension);
            presenter.SelectChipDestination(output);
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = format, Strict = strict });
            ChipExportPlan plan = Assert.IsType<ChipExportPlan>(presenter.PreparedChipPlan);
            Assert.True(plan.Report.OutputBytes > 0);
            Assert.True(plan.Report.WarningCount > 0);
            Assert.Contains("PanReduced", presenter.ChipReportText);
            Assert.Contains("track=0", presenter.ChipReportText);
            Assert.Equal(!strict, presenter.CanSaveChip);
            if (strict) { Assert.Contains("strict", presenter.ChipReportText); }
            await presenter.SaveChipAsync();
            Assert.Equal(!strict, File.Exists(output));
        }

        /// <summary>恒常的な制限だけなら strict でも保存でき、設定変更後は再診断を要求する。</summary>
        [Fact]
        public async Task InvalidatesPlanWhenSettingsChangeAndKeepsLimitationsSeparate()
        {
            using var fixture = new DawPresenterFixture();
            ExportPresenter presenter = fixture.Presenter.Export;
            presenter.SelectChipDestination(Path.ChangeExtension(fixture.Path, ".vgm"));
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm, Strict = true });
            Assert.True(presenter.CanSaveChip);
            Assert.NotEmpty(presenter.PreparedChipPlan!.Report.Limitations);
            Assert.Empty(presenter.PreparedChipPlan.Report.Warnings);
            presenter.InvalidateChipPlan();
            Assert.Null(presenter.PreparedChipPlan);
            Assert.False(presenter.CanSaveChip);
            await presenter.SaveChipAsync();
            Assert.False(File.Exists(presenter.ChipDestinationPath));
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = 0 });
            Assert.False(presenter.CanSaveChip);
            Assert.True(presenter.PreparedChipPlan!.Report.ErrorCount > 0);
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Nsf });
            Assert.Null(presenter.PreparedChipPlan);
            Assert.Contains("拡張子", presenter.StatusText);
        }

        /// <summary>既定の上書き拒否、OS の明示確定、診断中の新規競合を安全な保存 API へ渡す。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf, ".nsf", false, false)]
        [InlineData(ConversionFormat.Nsf, ".nsf", true, false)]
        [InlineData(ConversionFormat.Vgm, ".vgm", false, false)]
        [InlineData(ConversionFormat.Vgm, ".vgm", true, false)]
        [InlineData(ConversionFormat.Vgm, ".vgm", false, true)]
        public async Task ProtectsExistingFilesAndLateCreation(ConversionFormat format, string extension, bool overwrite, bool createLate)
        {
            using var fixture = new DawPresenterFixture();
            ExportPresenter presenter = fixture.Presenter.Export;
            string output = Path.ChangeExtension(fixture.Path, extension);
            if (!createLate) { File.WriteAllText(output, "keep"); }
            presenter.SelectChipDestination(output, overwrite);
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = format });
            if (createLate) { File.WriteAllText(output, "keep"); }
            await presenter.SaveChipAsync();
            if (overwrite)
            {
                Assert.NotEqual("keep", File.ReadAllText(output));
                Assert.Equal(output, presenter.LastExportedPath);
            }
            else
            {
                Assert.Equal("keep", File.ReadAllText(output));
                Assert.Contains("書き出し失敗", presenter.StatusText);
                Assert.Null(presenter.LastExportedPath);
                Assert.True(presenter.CanSaveChip);
            }
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, "*.tmp"));
        }

        /// <summary>I/O 失敗後も同じ plan で再試行でき、入力と同じパスは明示上書きでも拒否する。</summary>
        [Fact]
        public async Task RetriesSamePlanAfterFailureAndRejectsSourcePath()
        {
            using var fixture = new DawPresenterFixture();
            ExportPresenter presenter = fixture.Presenter.Export;
            string directory = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "missing");
            string output = Path.Combine(directory, "song.vgm");
            presenter.SelectChipDestination(output);
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            ChipExportPlan? plan = presenter.PreparedChipPlan;
            await presenter.SaveChipAsync();
            Assert.Same(plan, presenter.PreparedChipPlan);
            Assert.True(presenter.CanSaveChip);
            Directory.CreateDirectory(directory);
            await presenter.SaveChipAsync();
            Assert.True(File.Exists(output));
            string disguisedSource = Path.ChangeExtension(fixture.Path, ".vgm");
            File.Copy(fixture.Path, disguisedSource);
            fixture.Document.Open(disguisedSource);
            byte[] before = File.ReadAllBytes(disguisedSource);
            Assert.Throws<ArgumentException>(() => presenter.SelectChipDestination(disguisedSource, overwrite: true));
            Assert.Equal(before, File.ReadAllBytes(disguisedSource));
        }

        /// <summary>文書切替は保存候補と上書き許可を破棄するが通常の編集履歴を操作しない。</summary>
        [Fact]
        public async Task DocumentSwitchDiscardsPendingPlan()
        {
            using var fixture = new DawPresenterFixture();
            ExportPresenter presenter = fixture.Presenter.Export;
            string output = Path.ChangeExtension(fixture.Path, ".vgm");
            presenter.SelectChipDestination(output, overwrite: true);
            await presenter.PrepareChipAsync(new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.True(presenter.CanSaveChip);
            fixture.Presenter.Open(fixture.Path);
            Assert.Null(presenter.ChipDestinationPath);
            Assert.Null(presenter.PreparedChipPlan);
            Assert.False(presenter.CanSaveChip);
            await presenter.SaveChipAsync();
            Assert.False(File.Exists(output));
        }
    }
}
