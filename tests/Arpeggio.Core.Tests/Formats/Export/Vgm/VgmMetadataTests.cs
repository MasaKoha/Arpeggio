using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nes;
using Arpeggio.Formats.Export.Vgm;

namespace Arpeggio.Core.Tests.Formats.Export.Vgm
{
    /// <summary>GD3 の日本語・固定フィールド・長さ・入力拒否をバイト列から検証する。</summary>
    public sealed class VgmMetadataTests
    {
        private const int Gd3HeaderBytes = 12;
        private const int MetadataLimit = 1024;
        private const int JapaneseTitleField = 1;
        private const int JapaneseAuthorField = 7;

        /// <summary>日本語と補助平面の文字が UTF-16LE で残り、未指定欄・英語欄・日付は推測されない。</summary>
        [Fact]
        public void JapaneseTagHasElevenExactFieldsAndUtf16ByteLength()
        {
            const string Title = "星の旅🎵";
            const string Author = "作曲 太郎";
            const int ExpectedPayloadBytes = 116;
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            song.Title = Title;
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Author = Author });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            song.Title = "後から編集した題名";
            byte[] bytes = VgmWriterTests.Write(timeline, control.Timeline.Title, control.Report, Author);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(new[]
            {
                "", Title, "", "", "Nintendo Entertainment System", "", "", Author, "", "Arpeggio", ""
            }, parsed.Gd3Fields);
            Assert.Equal(0x100U, parsed.Gd3Version);
            Assert.Equal((uint)ExpectedPayloadBytes, parsed.Gd3PayloadBytes);
            Assert.Equal(parsed.Gd3Start + Gd3HeaderBytes + ExpectedPayloadBytes, bytes.Length);
            Assert.Equal(new byte[]
            {
                0, 0, 0x1F, 0x66, 0x6E, 0x30, 0xC5, 0x65, 0x3C, 0xD8, 0xB5, 0xDF, 0, 0
            }, bytes.Skip(parsed.Gd3Start + Gd3HeaderBytes).Take(14));
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>ASCII の曲名だけ英語欄へ複写し、著作者は ASCII でも原語欄だけに保存する。</summary>
        [Theory]
        [InlineData("Arpeggio 01", "Arpeggio 01")]
        [InlineData("", "")]
        [InlineData("Cafe\u00E9", "")]
        [InlineData("曲 ABC", "")]
        public void EnglishTitleIsPopulatedOnlyForAscii(string title, string englishTitle)
        {
            const string Author = "Composer";
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            byte[] bytes = VgmWriterTests.Write(timeline, title, report, Author);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal(new[] { englishTitle, title, "", "", "Nintendo Entertainment System", "", "", Author, "", "Arpeggio", "" }, parsed.Gd3Fields);
        }

        /// <summary>上限は文字数ではなく UTF-16 コード単位で判定し、最大長も切り詰めず保存する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MaximumMetadataLengthIsPreserved(bool supplementaryCharacters)
        {
            string title = supplementaryCharacters ? string.Concat(Enumerable.Repeat("🎵", MetadataLimit / 2)) : new string('題', MetadataLimit);
            string author = new string('A', MetadataLimit);
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            long? measured = VgmWriter.CalculateSize(timeline, title, author, report);
            byte[] bytes = VgmWriterTests.Write(timeline, title, report, author);
            ParsedVgm parsed = IndependentVgmParser.Parse(bytes);
            Assert.Equal((long)bytes.Length, measured);
            Assert.Equal(title, parsed.Gd3Fields[JapaneseTitleField]);
            Assert.Equal(author, parsed.Gd3Fields[JapaneseAuthorField]);
            Assert.Empty(report.Errors);
        }

        /// <summary>NUL・単独サロゲートはヘッダーより前に拒否し、既存 Stream の内容と位置を変えない。</summary>
        [Theory]
        [InlineData("曲\0名", "作者")]
        [InlineData("曲", "作\0者")]
        [InlineData(null, "作者")]
        [InlineData("曲", null)]
        public void InvalidMetadataDoesNotWriteAnything(string? title, string? author)
        {
            AssertMetadataRejected(title!, author!);
        }

        /// <summary>属性の UTF-8 シリアライズに依存せず、実際の単独 UTF-16 サロゲートを拒否する。</summary>
        [Theory]
        [InlineData(0xD800, true)]
        [InlineData(0xDC00, true)]
        [InlineData(0xD800, false)]
        [InlineData(0xDC00, false)]
        public void UnpairedSurrogateIsRejected(int character, bool invalidTitle)
        {
            string invalid = new string((char)character, 1);
            AssertMetadataRejected(invalidTitle ? invalid : "曲", invalidTitle ? "作者" : invalid);
        }

        /// <summary>タイトル・著作者それぞれの上限超過を拒否し、サイズ算定も成功にしない。</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExcessiveMetadataIsRejectedBeforeWriting(bool oversizedTitle)
        {
            string oversized = new string('あ', MetadataLimit + 1);
            AssertMetadataRejected(oversizedTitle ? oversized : "曲", oversizedTitle ? "作者" : oversized);
        }

        private static void AssertMetadataRejected(string title, string author)
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 2);
            (RegisterTimeline timeline, ConversionReport report) = VgmWriterTests.Compile(song);
            byte[] original = { 0x12, 0x34, 0x56 };
            using var destination = new MemoryStream();
            destination.Write(original);
            long originalPosition = destination.Position;
            Assert.False(VgmWriter.Write(destination, timeline, title, author, report));
            Assert.Equal(original, destination.ToArray());
            Assert.Equal(originalPosition, destination.Position);
            Assert.True(destination.CanWrite);
            Assert.Contains(report.Errors, error => error.Code == "InvalidMetadata");
            Assert.Null(VgmWriter.CalculateSize(timeline, title, author, report));
        }
    }
}
