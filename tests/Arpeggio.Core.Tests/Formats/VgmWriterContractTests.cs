using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>VGM の事前検証・Stream 所有権・非破壊な失敗契約を検証する。</summary>
    public sealed class VgmWriterContractTests
    {
        /// <summary>FileStream に実際の VGM を保存し、閉じた後にバイト列を再読込できる。</summary>
        [Fact]
        public void FileStreamWritesReadableNesVgm()
        {
            const int ConcertNote = 69;
            const int SongTicks = 4;
            string path = Path.Combine(Path.GetTempPath(), "Arpeggio-vgm-" + Guid.NewGuid().ToString("N") + ".vgm");
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: SongTicks);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = SongTicks, MidiNote = ConcertNote });
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            try
            {
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Assert.True(VgmWriter.Write(destination, timeline, "ファイル保存", "作曲者", report));
                    Assert.True(destination.CanWrite);
                }
                ParsedVgm parsed = IndependentVgmParser.Parse(File.ReadAllBytes(path));
                VgmWriterTests.AssertRoundTrip(timeline, parsed);
                Assert.Equal(report.OutputBytes, parsed.FileBytes);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>GB の形式欄だけを選んだ場合も NES のクロック・命令を混在させない。</summary>
        [Fact]
        public void GameBoyHeaderUsesOnlyItsOwnClockAndCommand()
        {
            Song song = SongFactory.Create(ChipKind.GameBoy, lengthTicks: 2);
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            byte[] bytes = VgmWriterTests.Write(timeline, song.Title, control.Report);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(0U, parsed.NesClock);
            Assert.Equal(4194304U, parsed.GameBoyClock);
            Assert.Equal("Nintendo Game Boy", parsed.Gd3Fields[4]);
            Assert.Equal(new byte[] { 0xB3, 0x16, 0, 0xB3, 0x16, 0x80 }, bytes.Skip(256).Take(6));
            VgmWriterTests.AssertRoundTrip(timeline, parsed);
        }

        /// <summary>Seek／Position／Length を使わず完全なファイルを書き、Stream を閉じない。</summary>
        [Fact]
        public void NonSeekableStreamReceivesCompleteFileAndRemainsOpen()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            using var destination = new VgmTestStream();
            long? measured = VgmWriter.CalculateSize(timeline, "曲", "作者", report);
            Assert.True(VgmWriter.Write(destination, timeline, "曲", "作者", report));
            byte[] bytes = destination.GetWrittenBytes();
            Assert.Equal((long)bytes.Length, measured);
            Assert.True(destination.CanWrite);
            VgmWriterTests.AssertRoundTrip(timeline, IndependentVgmParser.Parse(bytes));
        }

        /// <summary>相対オフセットは Stream 全体ではなく書き込み開始位置を基準にする。</summary>
        [Fact]
        public void PrefixDoesNotChangeFileRelativeOffsets()
        {
            byte[] prefix = { 0xCA, 0xFE, 0x01 };
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            byte[] standalone = VgmWriterTests.Write(timeline, song.Title, report);
            using var destination = new MemoryStream();
            destination.Write(prefix);
            Assert.True(VgmWriter.Write(destination, timeline, song.Title, "", report));
            Assert.Equal(prefix, destination.ToArray().Take(prefix.Length));
            Assert.Equal(standalone, destination.ToArray().Skip(prefix.Length));
            Assert.Equal((long)standalone.Length, report.OutputBytes);
        }

        /// <summary>保存前の全診断を使い、保持明細ゼロでも先行エラーと strict 警告を拒否する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingDiagnosticPreventsAnyWrite(bool warning)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, _) = VgmWriterTests.Compile(song);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict: warning, diagnosticDetailLimit: 0);
            var diagnostic = new ConversionDiagnostic("EarlierDiagnostic", "先行工程の診断。");
            if (warning)
            {
                report.AddWarning(diagnostic);
            }
            else
            {
                report.AddError(diagnostic);
            }
            using var destination = new VgmTestStream();
            Assert.Null(VgmWriter.CalculateSize(timeline, song.Title, "", report));
            Assert.False(VgmWriter.Write(destination, timeline, song.Title, "", report));
            Assert.Empty(destination.GetWrittenBytes());
            Assert.True(destination.CanWrite);
        }

        /// <summary>恒常的な制限だけなら strict でも保存でき、通常モードの警告も保存を妨げない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LimitationsAndNonStrictWarningsRemainWritable(bool strict)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, _) = VgmWriterTests.Compile(song);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict);
            report.AddLimitation("有限演奏の制限。");
            if (!strict)
            {
                report.AddWarning(new ConversionDiagnostic("EarlierWarning", "許容する変換警告。"));
            }
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
            VgmWriterTests.AssertRoundTrip(timeline, parsed);
        }

        /// <summary>I/O 例外は隠さず伝播し、部分出力も呼び出し元所有の Stream も維持する。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(260)]
        [InlineData(310)]
        public void IoFailureLeavesStreamOpenAndReportsPlannedSize(int failureAfterBytes)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            byte[] complete = VgmWriterTests.Write(timeline, song.Title, report);
            using var destination = new VgmTestStream(failureAfterBytes);
            Assert.Throws<IOException>(() => VgmWriter.Write(destination, timeline, song.Title, "", report));
            Assert.Equal(complete.Take(failureAfterBytes), destination.GetWrittenBytes());
            Assert.True(destination.CanWrite);
            Assert.Equal((long)complete.Length, report.OutputBytes);
        }

        /// <summary>異なるチップのレポートは引数ミスとして保存前に検出する。</summary>
        [Fact]
        public void MismatchedReportChipIsRejected()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, _) = VgmWriterTests.Compile(song);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy);
            using var destination = new MemoryStream();
            Assert.Throws<ArgumentException>(() => VgmWriter.Write(destination, timeline, song.Title, "", report));
            Assert.Empty(destination.ToArray());
        }

        /// <summary>VGM 以外のレポートでは別形式のサイズ検証へ流れず、変換エラーで拒否する。</summary>
        [Theory]
        [InlineData(ConversionFormat.None)]
        [InlineData(ConversionFormat.Nsf)]
        [InlineData(ConversionFormat.Midi)]
        public void NonVgmFormatIsRejected(ConversionFormat format)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, _) = VgmWriterTests.Compile(song);
            var report = new ConversionReport(format, ChipKind.Nes);
            using var destination = new MemoryStream();
            Assert.False(VgmWriter.Write(destination, timeline, song.Title, "", report));
            Assert.Contains(report.Errors, error => error.Code == "UnsupportedFormat");
            Assert.Empty(destination.ToArray());
        }

        /// <summary>読み取り専用 Stream は引数例外になり、呼び出し元の所有権を奪わない。</summary>
        [Fact]
        public void ReadOnlyStreamIsRejectedWithoutClosing()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            using var destination = new MemoryStream(Array.Empty<byte>(), writable: false);
            Assert.Throws<ArgumentException>(() => VgmWriter.Write(destination, timeline, song.Title, "", report));
            Assert.True(destination.CanRead);
        }
    }
}
