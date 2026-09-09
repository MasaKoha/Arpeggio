using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>任意定義の省略、既知版の完全保存、未知版の不透明保持を検証する。</summary>
    public sealed class SfxDefinitionSerializationTests
    {
        /// <summary>定義なし・明示 null の旧ファイルは同じバイト列で再保存される。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void MissingAndNullDefinitionsPreserveLegacyJson(ChipKind chip)
        {
            foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
            {
                Song legacy = SfxPresetFactory.Create(chip, preset.Kind);
                string original = SongSerializer.Serialize(legacy);
                Assert.DoesNotContain("\"sfx\"", original);
                JsonObject explicitNull = JsonNode.Parse(original)!.AsObject();
                explicitNull["sfx"] = null;
                Song restored = SongSerializer.Deserialize(explicitNull.ToJsonString());
                Assert.Null(restored.Sfx);
                Assert.Equal(Encoding.UTF8.GetBytes(original), Encoding.UTF8.GetBytes(SongSerializer.Serialize(restored)));
                Assert.Equal(SfxEditabilityReason.MissingDefinition, SfxSynchronization.Inspect(restored).Reason);
            }
        }

        /// <summary>全チップの全値・出自・両指紋が保存だけで変わらない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void KnownDefinitionsRoundTripWithAllProvenance(ChipKind chip)
        {
            SfxRandomization?[] randomizations =
            {
                null,
                new SfxRandomization { Operation = SfxRandomizationOperation.Randomize, Seed = uint.MaxValue, Category = "any" },
                new SfxRandomization
                {
                    Operation = SfxRandomizationOperation.Mutate, Seed = 0, Strength = 0.1,
                    Locks = new[] { "tone.baseFrequencyHz", "noise.envelope.volume" },
                    BaseParametersHash = new string('a', 64)
                }
            };
            foreach (SfxRandomization? randomization in randomizations)
            {
                Song song = SfxDocumentTestData.CreateSong(chip);
                SfxDefinitionData data = song.Sfx!.Known!;
                song.Sfx = new SfxDefinition(data with { LastRandomization = randomization });
                string original = SongSerializer.Serialize(song);
                Song restored = SongSerializer.Deserialize(original);
                SfxDefinitionData restoredData = Assert.IsType<SfxDefinitionData>(restored.Sfx!.Known);
                Assert.Equal(original, SongSerializer.Serialize(restored));
                Assert.Equal(data.Parameters, restoredData.Parameters);
                Assert.Equal(data.ParametersHash, restoredData.ParametersHash);
                Assert.Equal(data.GeneratedHash, restoredData.GeneratedHash);
                Assert.Equal("jump", restoredData.SourcePreset);
                Assert.True(SfxSynchronization.Inspect(restored).Editable);
                using JsonDocument document = JsonDocument.Parse(original);
                Assert.Equal("sfx", document.RootElement.EnumerateObject().Last().Name);
                Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
                Assert.Equal(new[] { "schemaVersion", "generatorVersion", "parameters", "parametersHash", "sourcePreset", "lastRandomization", "generatedHash" },
                    document.RootElement.GetProperty("sfx").EnumerateObject().Select(property => property.Name));
                Assert.DoesNotContain("unsupportedJson", original);
                Assert.DoesNotContain("known", original);
            }
        }

        /// <summary>現在チップだけを保存し、選択名と実数を仕様どおり正規化する。</summary>
        [Fact]
        public void NormalizesKnownValuesWithoutChangingHashesOrSourceObject()
        {
            Song song = SfxDocumentTestData.CreateSong();
            SfxDefinitionData data = song.Sfx!.Known!;
            SfxParameters parameters = data.Parameters with
            {
                Tone = data.Parameters.Tone with { BaseFrequencyHz = 440.1234565, SlideSemitonesPerSecond = -0.0000001 }
            };
            song.Sfx = new SfxDefinition(data with { Parameters = parameters, SourcePreset = null });
            string serialized = SongSerializer.Serialize(song);
            Song restored = SongSerializer.Deserialize(serialized);
            SfxDefinitionData restoredData = Assert.IsType<SfxDefinitionData>(restored.Sfx!.Known);
            Assert.Equal(440.123457, restoredData.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(restoredData.Parameters.Tone.SlideSemitonesPerSecond));
            Assert.Equal(440.1234565, parameters.Tone.BaseFrequencyHz);
            Assert.Same(parameters, song.Sfx.Known!.Parameters);
            Assert.Equal(data.ParametersHash, restoredData.ParametersHash);
            Assert.Contains("\"noiseMode\": \"long\"", serialized);
            Assert.Contains("\"sourcePreset\": null", serialized);
            Assert.Contains("\"lastRandomization\": null", serialized);
            Assert.DoesNotContain("\"gameBoy\"", serialized);
            Assert.DoesNotContain("\"snes\"", serialized);
            Assert.Equal(SfxEditabilityReason.SavedParametersChanged, SfxSynchronization.Inspect(restored).Reason);
        }

        /// <summary>未知構造・未知生成規則・未知乱数版は不完全な現在版データも含めて保持する。</summary>
        [Theory]
        [InlineData("{\"schemaVersion\":2,\"future\":{\"empty\":{},\"values\":[1,null,\"新形式\"]}}")]
        [InlineData("{\"schemaVersion\":1,\"generatorVersion\":2,\"parameters\":\"future shape\",\"custom\":true}")]
        [InlineData("{\"schemaVersion\":1,\"generatorVersion\":1,\"parameters\":null,\"lastRandomization\":{\"algorithmVersion\":2,\"operation\":\"future\",\"seed\":18446744073709551615},\"extra\":[2,1]}")]
        public void UnknownVersionsRemainOpaqueAfterDocumentDisposal(string unsupported)
        {
            JsonObject document = SfxDocumentTestData.ReadDocument(SfxDocumentTestData.CreateSong());
            document["sfx"] = JsonNode.Parse(unsupported);
            Song song = SongSerializer.Deserialize(document.ToJsonString());
            SfxDefinition definition = Assert.IsType<SfxDefinition>(song.Sfx);
            Assert.Null(definition.Known);
            Assert.True(definition.UnsupportedJson.HasValue);
            song.Title = "未知版を再保存";
            string saved = SongSerializer.Serialize(song);
            using JsonDocument restored = JsonDocument.Parse(saved);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(unsupported),
                JsonNode.Parse(restored.RootElement.GetProperty("sfx").GetRawText())));
            Assert.Equal(saved, SongSerializer.Serialize(SongSerializer.Deserialize(saved)));
            SfxSynchronizationState state = SfxSynchronization.Inspect(song);
            Assert.False(state.Editable);
            Assert.Equal(SfxEditabilityReason.UnsupportedSfxVersion, state.Reason);
            Assert.Null(state.Parameters);
            Assert.Null(state.SavedParameters);
        }

        /// <summary>キー順・字下げ・入力改行が違っても既知定義の正規保存は同じ。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void ReorderedKeysHaveIdenticalCanonicalOutput(ChipKind chip)
        {
            Song song = SfxDocumentTestData.CreateSong(chip);
            string expected = SongSerializer.Serialize(song);
            using JsonDocument document = JsonDocument.Parse(expected);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\r\n" }))
            {
                WriteReversed(writer, document.RootElement);
            }
            Song restored = SongSerializer.Deserialize(Encoding.UTF8.GetString(stream.ToArray()));
            Assert.Equal(expected, SongSerializer.Serialize(restored));
            Assert.True(SfxSynchronization.Inspect(restored).Editable);
        }

        /// <summary>実ファイル保存は BOM・末尾改行・一時ファイルを追加しない。</summary>
        [Fact]
        public void SaveAndLoadPreserveDefinitionBytes()
        {
            string directory = Path.Combine(Path.GetTempPath(), "arpeggio-sfx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "known.arpeggio.json");
                Song song = SfxDocumentTestData.CreateSong();
                SongSerializer.Save(song, path);
                Assert.Equal(Encoding.UTF8.GetBytes(SongSerializer.Serialize(song)), File.ReadAllBytes(path));
                Assert.True(SfxSynchronization.Inspect(SongSerializer.Load(path)).Editable);
                Assert.Single(Directory.GetFiles(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void WriteReversed(Utf8JsonWriter writer, JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().Reverse())
                {
                    writer.WritePropertyName(property.Name);
                    WriteReversed(writer, property.Value);
                }
                writer.WriteEndObject();
                return;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteReversed(writer, item);
                }
                writer.WriteEndArray();
                return;
            }
            element.WriteTo(writer);
        }
    }
}
