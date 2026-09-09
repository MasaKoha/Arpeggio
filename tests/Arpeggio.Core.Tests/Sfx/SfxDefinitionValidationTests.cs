using System;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>既知版の不完全データを既定値で救済せず、文書エラーとして拒否する。</summary>
    public sealed class SfxDefinitionValidationTests
    {
        /// <summary>全チップの全保存パラメータは一つも省略できない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void EveryParameterMustBePresent(ChipKind chip)
        {
            Song song = SfxDocumentTestData.CreateSong(chip);
            foreach (SfxParameterDescription description in SfxParameterCatalog.GetAll(chip))
            {
                JsonObject document = SfxDocumentTestData.ReadDocument(song);
                JsonObject parameters = SfxDocumentTestData.Definition(document)["parameters"]!.AsObject();
                JsonObject parent = SfxDocumentTestData.Parent(parameters, description.Path, out string name);
                Assert.True(parent.Remove(name));
                Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
            }
        }

        /// <summary>定義の必須キーには null 許可項目も含め、欠落を検出する。</summary>
        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("generatorVersion")]
        [InlineData("parameters")]
        [InlineData("parametersHash")]
        [InlineData("sourcePreset")]
        [InlineData("lastRandomization")]
        [InlineData("generatedHash")]
        public void DefinitionKeysCannotBeOmitted(string name)
        {
            JsonObject document = SfxDocumentTestData.ReadDocument(SfxDocumentTestData.CreateSong());
            Assert.True(SfxDocumentTestData.Definition(document).Remove(name));
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
        }

        /// <summary>既知版の型・範囲・未知キー・チップ違い・出自を厳密に検証する。</summary>
        [Theory]
        [InlineData("schemaVersion", "0")]
        [InlineData("schemaVersion", "-1")]
        [InlineData("schemaVersion", "1.5")]
        [InlineData("schemaVersion", "\"1\"")]
        [InlineData("schemaVersion", "null")]
        [InlineData("generatorVersion", "0")]
        [InlineData("generatorVersion", "null")]
        [InlineData("parameters", "null")]
        [InlineData("parameters", "[]")]
        [InlineData("parameters.tone", "{}")]
        [InlineData("parameters.tone.envelope", "null")]
        [InlineData("parameters.tone.baseFrequencyHz", "12001")]
        [InlineData("parameters.tone.baseFrequencyHz", "1e999")]
        [InlineData("parameters.tone.enabled", "false")]
        [InlineData("parameters.tone.unknown", "0")]
        [InlineData("parameters.nes.noiseMode", "\"Long\"")]
        [InlineData("parameters.nes.noiseMode", "1")]
        [InlineData("parameters.gameBoy", "null")]
        [InlineData("parametersHash", "null")]
        [InlineData("parametersHash", "\"short\"")]
        [InlineData("generatedHash", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
        [InlineData("sourcePreset", "\"power-up\"")]
        [InlineData("sourcePreset", "\"strings\"")]
        [InlineData("sourcePreset", "1")]
        [InlineData("lastRandomization", "{}")]
        [InlineData("unknown", "1")]
        public void InvalidKnownFieldsAreDocumentErrors(string path, string value)
        {
            JsonObject document = SfxDocumentTestData.ReadDocument(SfxDocumentTestData.CreateSong());
            JsonObject parent = SfxDocumentTestData.Parent(SfxDocumentTestData.Definition(document), path, out string name);
            parent[name] = JsonNode.Parse(value);
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
        }

        /// <summary>ソングのチップを正とし、他チップの完全パラメータも拒否する。</summary>
        [Fact]
        public void ParametersMustMatchSongChip()
        {
            JsonObject document = SfxDocumentTestData.ReadDocument(SfxDocumentTestData.CreateSong());
            JsonObject other = SfxDocumentTestData.ReadDocument(SfxDocumentTestData.CreateSong(ChipKind.GameBoy));
            document["sfx"] = other["sfx"]!.DeepClone();
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
        }

        /// <summary>重複はエスケープで同じ名前になったキーも含めて拒否する。</summary>
        [Theory]
        [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")]
        [InlineData("\"generatorVersion\": 1", "\"generatorVersion\": 1, \"generatorVersion\": 1")]
        [InlineData("\"baseFrequencyHz\": 440", "\"baseFrequencyHz\": 440, \"baseFrequencyHz\": 441")]
        [InlineData("\"enabled\": true", "\"enabled\": true, \"\\u0065nabled\": false")]
        [InlineData("\"sourcePreset\": \"jump\"", "\"sourcePreset\": \"jump\", \"sourcePreset\": null")]
        [InlineData("\"sfx\": {", "\"sfx\": null, \"sfx\": {")]
        public void DuplicateKeysAreRejected(string original, string replacement)
        {
            string serialized = SongSerializer.Serialize(SfxDocumentTestData.CreateSong());
            Assert.Contains(original, serialized);
            string invalid = serialized.Replace(original, replacement, StringComparison.Ordinal);
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(invalid));
        }

        /// <summary>既知の変異出自にも欠落・型違い・適用外項目・不正ロックを許さない。</summary>
        [Theory]
        [InlineData("operation", "\"Mutate\"")]
        [InlineData("operation", "\"none\"")]
        [InlineData("operation", "2")]
        [InlineData("algorithmVersion", "0")]
        [InlineData("seed", "-1")]
        [InlineData("seed", "4294967296")]
        [InlineData("seed", "0.5")]
        [InlineData("strength", "null")]
        [InlineData("strength", "1.1")]
        [InlineData("category", "\"any\"")]
        [InlineData("locks", "null")]
        [InlineData("locks", "[null]")]
        [InlineData("locks", "[\"tone\"]")]
        [InlineData("locks", "[\"gameBoy.noiseWidth\"]")]
        [InlineData("baseParametersHash", "null")]
        [InlineData("unknown", "true")]
        public void InvalidRandomizationIsDocumentError(string name, string value)
        {
            JsonObject document = CreateMutatedDocument();
            SfxDocumentTestData.Definition(document)["lastRandomization"]![name] = JsonNode.Parse(value);
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
        }

        /// <summary>seed=0 や空 locks を既定値補完の結果として受理しない。</summary>
        [Theory]
        [InlineData("operation")]
        [InlineData("algorithmVersion")]
        [InlineData("seed")]
        [InlineData("category")]
        [InlineData("strength")]
        [InlineData("locks")]
        [InlineData("baseParametersHash")]
        public void RandomizationKeysCannotBeOmitted(string name)
        {
            JsonObject document = CreateMutatedDocument();
            Assert.True(SfxDocumentTestData.Definition(document)["lastRandomization"]!.AsObject().Remove(name));
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
        }

        /// <summary>カテゴリ生成の保存出自は正式カテゴリと適用外の null を要求する。</summary>
        [Theory]
        [InlineData("category", "null")]
        [InlineData("category", "\"pickup\"")]
        [InlineData("category", "\"unknown\"")]
        [InlineData("strength", "0.1")]
        [InlineData("locks", "[]")]
        [InlineData("baseParametersHash", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
        public void RandomizeRejectsInvalidCategoryAndInapplicableFields(string name, string value)
        {
            Song song = SfxDocumentTestData.CreateSong();
            song.Sfx = new SfxDefinition(song.Sfx!.Known! with
            {
                LastRandomization = new SfxRandomization { Operation = SfxRandomizationOperation.Randomize, Category = "jump" }
            });
            JsonObject document = SfxDocumentTestData.ReadDocument(song);
            SfxDocumentTestData.Definition(document)["lastRandomization"]![name] = JsonNode.Parse(value);
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
        }

        /// <summary>直接構築した不正値も SongValidator と保存の両方が文書エラーにする。</summary>
        [Fact]
        public void InMemoryInvalidParametersAreRejectedWithoutMutation()
        {
            Song song = SfxDocumentTestData.CreateSong();
            SfxDefinitionData data = song.Sfx!.Known!;
            SfxParameters invalid = data.Parameters with { Tone = data.Parameters.Tone with { BaseFrequencyHz = double.NaN } };
            song.Sfx = new SfxDefinition(data with { Parameters = invalid });
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            Assert.Throws<SongValidationException>(() => SongSerializer.Serialize(song));
            Assert.Same(invalid, song.Sfx.Known!.Parameters);
        }

        private static JsonObject CreateMutatedDocument()
        {
            Song song = SfxDocumentTestData.CreateSong();
            SfxDefinitionData data = song.Sfx!.Known!;
            song.Sfx = new SfxDefinition(data with
            {
                LastRandomization = new SfxRandomization
                {
                    Operation = SfxRandomizationOperation.Mutate, Strength = 0.1,
                    Locks = Array.Empty<string>(), BaseParametersHash = data.ParametersHash
                }
            });
            return SfxDocumentTestData.ReadDocument(song);
        }
    }
}
