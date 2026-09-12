using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Storage;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx.Storage
{
    /// <summary>独立した正規 JSON の固定指紋と、環境非依存の保存領域を検証する。</summary>
    public sealed class SfxHashTests
    {
        /// <summary>旧 NES JSON の固定期待値は optional sfx 追加後も変わらない。</summary>
        [Fact]
        public void LegacyCanonicalJsonHasNoByteChanges()
        {
            string expected = SfxCanonicalJsonExamples.LegacyNes.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
            string actual = SongSerializer.Serialize(SongFactory.Create(ChipKind.Nes));
            Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
            Assert.Equal(expected, SongSerializer.Serialize(SongSerializer.Deserialize(expected)));
        }

        /// <summary>2スペース・LF・BOMなし・末尾改行なしの全パラメータから求めた固定 SHA-256。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, "ad3a5bd611b19d9a8de48bdf076ac2b185eac4c0210bd6f7619afe1d87505450")]
        [InlineData(ChipKind.GameBoy, "c91b7a98baeecc283f866922b25e1962b2758b4f58e283c8e18c52cbb2e715c9")]
        [InlineData(ChipKind.Snes, "0d35f492c3dbe2ce8561738c7b1f69027291a385e86cb0c8b540b8e92e410a7e")]
        public void ParameterHashMatchesIndependentGolden(ChipKind chip, string expected)
        {
            Assert.Equal(expected, SfxHash.ComputeParametersHash(SfxParameterCatalog.CreateDefaults(chip), chip));
        }

        /// <summary>保存と指紋でパラメータ表現が一致し、周囲の sfx メタデータに依存しない。</summary>
        [Fact]
        public void ParameterHashUsesVersionsAndCanonicalSavedValues()
        {
            Song song = SfxDocumentTestData.CreateSong();
            SfxDefinitionData data = song.Sfx!.Known!;
            using JsonDocument document = JsonDocument.Parse(SongSerializer.Serialize(song));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteNumber("generatorVersion", 1);
                writer.WritePropertyName("parameters");
                document.RootElement.GetProperty("sfx").GetProperty("parameters").WriteTo(writer);
                writer.WriteEndObject();
            }
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(stream.ToArray())), data.ParametersHash);
            Assert.NotEqual(data.ParametersHash, SfxHash.ComputeParametersHash(data.Parameters, song.Chip, schemaVersion: 2));
            Assert.NotEqual(data.ParametersHash, SfxHash.ComputeParametersHash(data.Parameters, song.Chip, generatorVersion: 2));
            SfxParameters sameAfterRounding = data.Parameters with
            {
                Tone = data.Parameters.Tone with { BaseFrequencyHz = 440.0000001 }
            };
            Assert.Equal(data.ParametersHash, SfxHash.ComputeParametersHash(sameAfterRounding, song.Chip));
            SfxParameters disabledNoiseEdited = data.Parameters with
            {
                Noise = data.Parameters.Noise with { Envelope = data.Parameters.Noise.Envelope with { Volume = 1 } }
            };
            Assert.NotEqual(data.ParametersHash, SfxHash.ComputeParametersHash(disabledNoiseEdited, song.Chip));
        }

        /// <summary>生成領域は旧保存仕様の固定順を継承し、title を完全に除外する。</summary>
        [Fact]
        public void GeneratedHashMatchesIndependentGolden()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            const string Expected = "fcd1e02aa6a4720d8d2555afbaaa9e67cdb9eea637bb38412057216fe9ebfd77";
            Assert.Equal(Expected, SfxHash.ComputeGeneratedHash(song));
            song.Title = "別の名前\r\n二行目";
            Assert.Equal(Expected, SfxHash.ComputeGeneratedHash(song));
        }

        /// <summary>revision には title・出自・sfx を含め、生成指紋には含めない。</summary>
        [Fact]
        public void RevisionIncludesExcludedMetadata()
        {
            Song song = SfxDocumentTestData.CreateSong();
            string generated = SfxHash.ComputeGeneratedHash(song);
            string original = SfxHash.ComputeRevision(song);
            song.Title = "別タイトル";
            string renamed = SfxHash.ComputeRevision(song);
            Assert.NotEqual(original, renamed);
            Assert.Equal(generated, SfxHash.ComputeGeneratedHash(song));
            song.Sfx = new SfxDefinition(song.Sfx!.Known! with { SourcePreset = "coin" });
            string changedSource = SfxHash.ComputeRevision(song);
            Assert.NotEqual(renamed, changedSource);
            Assert.Equal(generated, SfxHash.ComputeGeneratedHash(song));
            song.Sfx = null;
            Assert.NotEqual(changedSource, SfxHash.ComputeRevision(song));
            Assert.Equal(generated, SfxHash.ComputeGeneratedHash(song));
        }
    }
}
