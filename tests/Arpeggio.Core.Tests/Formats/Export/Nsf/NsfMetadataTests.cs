using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Nsf;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>ASCII 31 byte 制約・Unicode scalar 置換・strict と不正メタデータの保存前拒否を検証する。</summary>
    public sealed class NsfMetadataTests
    {
        /// <summary>ASCII の最大長と切り詰めを、三欄すべてで独立ロードする。</summary>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(31, 0)]
        [InlineData(32, 3)]
        [InlineData(1024, 3)]
        public void AsciiFieldsAreTerminatedAndReducedOnlyWhenNecessary(int length, long warnings)
        {
            string title = new string('T', length);
            string author = new string('A', length);
            string copyright = new string('C', length);
            var (bytes, file, report) = NsfWriterFixture.Save(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()), title, author, copyright);
            Assert.Equal(new string('T', Math.Min(length, 31)), file.Title);
            Assert.Equal(new string('A', Math.Min(length, 31)), file.Author);
            Assert.Equal(new string('C', Math.Min(length, 31)), file.Copyright);
            Assert.Equal((byte)0, bytes[0x2D]);
            Assert.Equal((byte)0, bytes[0x4D]);
            Assert.Equal((byte)0, bytes[0x6D]);
            Assert.Equal(warnings, report.WarningCount);
            if (warnings != 0)
            {
                ConversionDiagnostic warning = Assert.Single(report.Warnings);
                Assert.Equal("MetadataReduced", warning.Code);
                Assert.Equal(warnings, warning.OccurrenceCount);
                Assert.NotNull(warning.Original);
                Assert.Contains("title: ", warning.Original);
                Assert.Contains("author: ", warning.Original);
                Assert.Contains("copyright: ", warning.Original);
            }
        }

        /// <summary>補助平面文字を surrogate 二文字ではなく一 scalar の疑問符へ変換してから切り詰める。</summary>
        [Fact]
        public void NonAsciiScalarsAreReplacedBeforeTruncation()
        {
            string title = new string('A', 29) + "😀音Z";
            var (_, file, report) = NsfWriterFixture.Save(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()), title, "作曲😀", "©2026");
            Assert.Equal(new string('A', 29) + "??", file.Title);
            Assert.Equal("???", file.Author);
            Assert.Equal("?2026", file.Copyright);
            Assert.Equal(3L, report.WarningCount);
            Assert.Equal(3L, Assert.Single(report.Warnings).OccurrenceCount);
        }

        /// <summary>NUL・過大長・単独 surrogate は切り詰め位置の後でも保存前に拒否する。</summary>
        [Theory]
        [InlineData("title")]
        [InlineData("author")]
        [InlineData("copyright")]
        public void InvalidMetadataDoesNotWriteAnyBytes(string field)
        {
            var (data, _) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()));
            foreach (string invalid in new[] { "A\0B", new string('A', 1025), "\uD800", "\uDC00", new string('A', 32) + "\uD800", null! })
            {
                using var destination = new MemoryStream();
                destination.WriteByte(0x42);
                ConversionReport report = NsfExecutionFixture.CreateReport();
                Assert.False(NsfWriter.Write(destination, data, field == "title" ? invalid : "valid", report,
                    field == "author" ? invalid : "", field == "copyright" ? invalid : ""));
                Assert.Equal(new byte[] { 0x42 }, destination.ToArray());
                Assert.Contains(report.Errors, diagnostic => diagnostic.Code == "InvalidMetadata");
                Assert.True(destination.CanWrite);
            }
        }

        /// <summary>診断明細を保持しなくても新規の縮約警告を strict が保存前に拒否する。</summary>
        [Fact]
        public void StrictRejectsNewMetadataReductionWithZeroDetailLimit()
        {
            var (data, _) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()));
            using var destination = new MemoryStream();
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            Assert.False(NsfWriter.Write(destination, data, "曲😀", report));
            Assert.Empty(destination.ToArray());
            Assert.Empty(report.Warnings);
            Assert.Equal(1L, report.WarningCount);
            Assert.Equal(1L, report.WarningCountsByCode["MetadataReduced"]);
        }
    }
}
