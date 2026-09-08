using System;
using System.IO;
using System.Linq;
using System.Threading;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Cli;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>確定済み plan の所有権・保存保護・失敗時清掃を検証する。</summary>
    public sealed class ChipExportServiceTests
    {
        /// <summary>元 Song の後編集や複数回保存で、演奏 byte 列とレポートは変わらない。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Vgm)]
        public void PlanOwnsPreparedContentsAndDoesNotRepeatDiagnostics(ConversionFormat format)
        {
            Song song = CreateSong();
            song.Title = "曲";
            ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = format });
            Assert.True(plan.CanWrite);
            string report = SessionOutput.Serialize(plan.Report);
            song.Title = "edited";
            song.Tracks[0].Notes.Clear();
            using var first = new VgmTestStream();
            using var second = new VgmTestStream();
            Assert.True(ChipExportService.Write(plan, first));
            Assert.True(ChipExportService.Write(plan, second));
            Assert.True(first.CanWrite);
            Assert.Equal(first.GetWrittenBytes(), second.GetWrittenBytes());
            Assert.Equal((long)first.GetWrittenBytes().Length, plan.Report.OutputBytes);
            Assert.Equal(report, SessionOutput.Serialize(plan.Report));
        }

        /// <summary>strict の最終コピーは保持上限後の件数と予定サイズを失わない。</summary>
        [Fact]
        public void StrictReportPreservesDroppedDiagnosticCounts()
        {
            const int NoteCount = 4100;
            const int NoteSpacing = 4;
            const int NoteDuration = 2;
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: NoteCount * NoteSpacing);
            for (int index = 0; index < NoteCount; index++)
            {
                song.Tracks[0].Notes.Add(new Note { Tick = index * NoteSpacing, DurationTicks = NoteDuration, MidiNote = 0 });
            }
            ChipExportPlan plan = ChipExportService.Prepare(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Strict = true });
            Assert.False(plan.CanWrite);
            Assert.True(plan.Report.OutputBytes > 0);
            Assert.True(plan.Report.DroppedWarningCount > 0);
            Assert.Equal(plan.Report.WarningCount, plan.Report.WarningCountsByCode.Values.Sum());
            Assert.Equal(plan.Report.WarningCount, plan.Report.Warnings.Sum(warning => warning.OccurrenceCount) + plan.Report.DroppedWarningCount);
            using var stream = new MemoryStream();
            Assert.False(ChipExportService.Write(plan, stream));
            Assert.Equal(0, stream.Length);
        }

        /// <summary>移動段階の I/O 失敗と事前キャンセルでは既存出力を保ち一時ファイルを残さない。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Vgm)]
        public void FileFailuresAndCancellationKeepDestination(ConversionFormat format)
        {
            using var fixture = new CliConversionFixture();
            ChipExportPlan plan = ChipExportService.Prepare(CreateSong(), new ChipExportOptions { Format = format });
            string output = fixture.PathFor("occupied");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "keep"), "original");
            Exception? failure = Record.Exception(() => ChipExportService.Write(plan, output, overwrite: true));
            Assert.NotNull(failure);
            Assert.Contains(failure.GetType(), new[] { typeof(IOException), typeof(UnauthorizedAccessException) });
            Assert.Equal("original", File.ReadAllText(Path.Combine(output, "keep")));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
            string file = fixture.PathFor("existing");
            File.WriteAllText(file, "original");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => ChipExportService.Write(plan, file, true, cancellation.Token));
            Assert.Equal("original", File.ReadAllText(file));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        /// <summary>部分 Stream の I/O 例外を伝播し、Stream を閉じずレポートを変えない。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Vgm)]
        public void PartialStreamFailurePreservesOwnershipAndReport(ConversionFormat format)
        {
            const int FailurePosition = 32;
            ChipExportPlan plan = ChipExportService.Prepare(CreateSong(), new ChipExportOptions { Format = format });
            string before = SessionOutput.Serialize(plan.Report);
            using var stream = new VgmTestStream(FailurePosition);
            Assert.Throws<IOException>(() => ChipExportService.Write(plan, stream));
            Assert.True(stream.CanWrite);
            Assert.Equal(FailurePosition, stream.GetWrittenBytes().Length);
            Assert.Equal(before, SessionOutput.Serialize(plan.Report));
        }

        /// <summary>入力同一パスは上書き指定でも拒否し、変換エラーの plan はファイルを作らない。</summary>
        [Theory]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Vgm)]
        public void RejectsSourcePathAndInvalidPlans(ConversionFormat format)
        {
            using var fixture = new CliConversionFixture();
            string source = fixture.CreateSong();
            byte[] before = File.ReadAllBytes(source);
            ChipExportPlan plan = ChipExportService.Prepare(CreateSong(), new ChipExportOptions { Format = format });
            Assert.Throws<ArgumentException>(() => ChipExportService.Write(plan, source, true, sourcePath: source));
            Assert.Equal(before, File.ReadAllBytes(source));
            ChipExportPlan invalid = ChipExportService.Prepare(CreateSong(), new ChipExportOptions { Format = format, Loops = 0 });
            Assert.False(ChipExportService.Write(invalid, fixture.PathFor("missing/output")));
            Assert.False(Directory.Exists(fixture.PathFor("missing")));
        }

        private static Song CreateSong()
        {
            const int Tempo = 120;
            const int LengthTicks = 96;
            const int DurationTicks = 48;
            Song song = SongFactory.Create(ChipKind.Nes, Tempo, LengthTicks);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = DurationTicks, MidiNote = 69 });
            return song;
        }
    }
}
