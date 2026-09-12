using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Brief
{
    /// <summary>保存形式・読み込み検証・失敗時の原本維持を固定する。</summary>
    public sealed class CompositionBriefFileTests
    {
        /// <summary>全項目の往復で複数行や空白を保ち、規定順と文字列 enum で保存する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void RoundTripPreservesAllFieldsAndFormat(ChipKind chip)
        {
            using var fixture = new BriefFileFixture();
            string path = fixture.PathFor("song.brief.json");
            var brief = new CompositionBrief
            {
                Title = "廃墟の朝", Chip = chip, TempoBpm = 96,
                Mood = "静か\n希望", Structure = "導入\r\n主題", Instrumentation = "  主旋律と低音  ",
                References = "参考曲", Constraints = "ループ", Notes = string.Empty
            };
            CompositionBriefFile.Create(brief, path);
            Assert.Equal(brief, CompositionBriefFile.Load(path));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(new[] { "version", "title", "chip", "tempoBpm", "mood", "structure", "instrumentation", "references", "constraints", "notes" },
                document.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal(chip.ToString(), document.RootElement.GetProperty("chip").GetString());
            Assert.Equal(CompositionBrief.CurrentVersion, document.RootElement.GetProperty("version").GetInt32());
            Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.False(Directory.Exists(path + ".history"));
        }

        /// <summary>未指定は JSON の null として保存し、自由記述だけ読み込み時に空文字へ揃える。</summary>
        [Fact]
        public void MissingAndNullTextAreNormalizedOnlyWhenReading()
        {
            var brief = new CompositionBrief { Title = "仮題" };
            using JsonDocument document = JsonDocument.Parse(CompositionBriefFile.Serialize(brief));
            string[] optionalNames = { "chip", "tempoBpm", "mood", "structure", "instrumentation", "references", "constraints", "notes" };
            foreach (string name in optionalNames)
            {
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty(name).ValueKind);
            }
            CompositionBrief missing = CompositionBriefFile.Deserialize("{\"version\":1,\"title\":\"仮題\"}");
            CompositionBrief explicitNull = CompositionBriefFile.Deserialize(document.RootElement.GetRawText());
            Assert.Equal(missing, explicitNull);
            Assert.Null(missing.Chip);
            Assert.Null(missing.TempoBpm);
            Assert.All(new[] { missing.Mood, missing.Structure, missing.Instrumentation, missing.References, missing.Constraints, missing.Notes },
                value => Assert.Equal(string.Empty, value));
        }

        /// <summary>欠損・重複・未来版と不正な JSON 型を文書エラーとして拒否する。</summary>
        [Theory]
        [InlineData("{", "document")]
        [InlineData("null", "document")]
        [InlineData("[]", "document")]
        [InlineData("{}", "version")]
        [InlineData("{\"title\":\"曲\"}", "version")]
        [InlineData("{\"version\":null}", "version")]
        [InlineData("{\"version\":\"1\"}", "version")]
        [InlineData("{\"version\":1.5}", "version")]
        [InlineData("{\"version\":2147483648}", "version")]
        [InlineData("{\"version\":2}", "version")]
        [InlineData("{\"version\":1,\"version\":1}", "version")]
        [InlineData("{\"version\":1}", "title")]
        [InlineData("{\"version\":1,\"title\":null}", "title")]
        [InlineData("{\"version\":1,\"title\":\" \"}", "title")]
        [InlineData("{\"version\":1,\"title\":42}", "document")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"mood\":[]}", "document")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"tempoBpm\":0}", "tempoBpm")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"chip\":\"None\"}", "chip")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"chip\":99}", "chip")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"chip\":\"unknown\"}", "document")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"unknown\":null}", "unknown")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"title\":\"別の曲\"}", "title")]
        [InlineData("{\"version\":1,\"title\":\"曲\",\"notes\":null,\"notes\":\"追記\"}", "notes")]
        public void InvalidDocumentIsRejected(string json, string parameterPath)
        {
            CompositionBriefException exception = Assert.Throws<CompositionBriefException>(() => CompositionBriefFile.Deserialize(json));
            Assert.Equal("InvalidBrief", exception.Code);
            Assert.Equal(parameterPath, exception.ParameterPath);
        }

        /// <summary>文字数違反も読み込み時には文書エラーとし、整数 enum は既存保存規約に従い受理する。</summary>
        [Fact]
        public void ReadValidationAndIntegerChipFollowStorageContract()
        {
            string json = JsonSerializer.Serialize(new { version = 1, title = "曲", notes = new string('あ', CompositionBriefValidator.MaximumTextLength + 1) });
            CompositionBriefException exception = Assert.Throws<CompositionBriefException>(() => CompositionBriefFile.Deserialize(json));
            Assert.Equal("InvalidBrief", exception.Code);
            Assert.Equal("notes", exception.ParameterPath);
            CompositionBrief brief = CompositionBriefFile.Deserialize("{\"version\":1,\"title\":\"曲\",\"chip\":2}");
            Assert.Equal(ChipKind.GameBoy, brief.Chip);
        }

        /// <summary>検証失敗と作成先の重複は原本を変えず、一時ファイルを残さない。</summary>
        [Fact]
        public void RejectedSaveAndDuplicateCreateKeepOriginalBytes()
        {
            using var fixture = new BriefFileFixture();
            string path = fixture.PathFor("song.brief.json");
            var brief = new CompositionBrief { Title = "元の曲" };
            CompositionBriefFile.Create(brief, path);
            byte[] before = File.ReadAllBytes(path);
            CompositionBriefException invalid = Assert.Throws<CompositionBriefException>(() =>
                CompositionBriefFile.Save(brief with { Title = string.Empty }, path));
            Assert.Equal("InvalidParameter", invalid.Code);
            CompositionBriefException duplicate = Assert.Throws<CompositionBriefException>(() =>
                CompositionBriefFile.Create(brief with { Title = "別の曲" }, path));
            Assert.Equal("DestinationExists", duplicate.Code);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
            CompositionBriefFile.Save(brief with { Title = "編集後", Notes = "追記" }, path);
            Assert.Equal("編集後", CompositionBriefFile.Load(path).Title);
            Assert.Equal("追記", CompositionBriefFile.Load(path).Notes);
        }

        /// <summary>置換先がディレクトリの場合も失敗後に一時ファイルを削除する。</summary>
        [Fact]
        public void FailedMoveRemovesTemporaryFileAndPreservesDestination()
        {
            using var fixture = new BriefFileFixture();
            string path = fixture.PathFor("blocked.brief.json");
            Directory.CreateDirectory(path);
            string marker = Path.Combine(path, "keep.txt");
            File.WriteAllText(marker, "保持");
            Exception? exception = Record.Exception(() => CompositionBriefFile.Save(new CompositionBrief { Title = "曲" }, path));
            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.Equal("保持", File.ReadAllText(marker));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }
    }
}
