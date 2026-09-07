using System;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Instruments
{
    /// <summary>埋め込みサンプルの JSON 互換性と音色検証の境界。</summary>
    public sealed class SnesSampleInstrumentTests
    {
        /// <summary>旧音色は既定値を使い、追加後も version 1 の Base64 として往復する。</summary>
        [Fact]
        public void Json_PreservesVersionAndLegacyDefaults()
        {
            SnesSampleInstrument legacy = Assert.IsType<SnesSampleInstrument>(
                InstrumentJson.Deserialize("{\"id\":1,\"name\":\"legacy\",\"kind\":\"SnesSample\"}"));
            Assert.Null(legacy.SampleData);
            Assert.Equal(60, legacy.RootMidiNote);
            Assert.Equal(44100, legacy.SampleRate);
            Assert.Equal(0, legacy.LoopStart);
            Assert.Equal(0, legacy.LoopEnd);
            Song song = SongFactory.Create(ChipKind.Snes);
            song.Instruments[0] = legacy;
            string legacyJson = SongSerializer.Serialize(song);
            Assert.Contains("\"version\": 1", legacyJson);
            JsonObject legacyDocument = JsonNode.Parse(legacyJson)!.AsObject();
            JsonObject legacyInstrument = legacyDocument["instruments"]![0]!.AsObject();
            foreach (string property in new[] { "sampleData", "sampleRate", "rootMidiNote", "loopStart", "loopEnd" })
            {
                legacyInstrument.Remove(property);
            }
            SnesSampleInstrument restoredLegacy = (SnesSampleInstrument)SongSerializer.Deserialize(legacyDocument.ToJsonString()).Instruments[0];
            Assert.Null(restoredLegacy.SampleData);
            Assert.Equal(60, restoredLegacy.RootMidiNote);
            legacy.SampleData = SampleDataCodec.Encode(new short[] { short.MinValue, short.MaxValue });
            legacy.RootMidiNote = 72;
            legacy.SampleRate = 22050;
            string json = SongSerializer.Serialize(song);
            Song restored = SongSerializer.Deserialize(json);
            SnesSampleInstrument sample = Assert.IsType<SnesSampleInstrument>(restored.Instruments[0]);
            Assert.Equal(legacy.SampleData, sample.SampleData);
            Assert.Equal(72, sample.RootMidiNote);
            Assert.Equal(22050, sample.SampleRate);
            Assert.Equal(2, sample.SampleCount);
            Assert.Equal(json, SongSerializer.Serialize(restored));
            Assert.DoesNotContain("decodedSamples", json);
            sample.SampleData = null;
            Assert.Equal(0, sample.SampleCount);
        }

        /// <summary>サンプル不正は代入時でなく InstrumentValidator を通るソング検証で拒否する。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("!!!!")]
        [InlineData("AA==")]
        public void Validator_RejectsMalformedSample(string data)
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            ((SnesSampleInstrument)song.Instruments[0]).SampleData = data;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>範囲の空・逆転・末尾超過・不正レート・基準音を拒否する。</summary>
        [Theory]
        [InlineData(0, 0, 0, 60)]
        [InlineData(-1, 0, 22050, 60)]
        [InlineData(2, 2, 22050, 60)]
        [InlineData(0, 5, 22050, 60)]
        [InlineData(4, 0, 22050, 60)]
        [InlineData(0, -1, 22050, 60)]
        [InlineData(0, 0, 22050, -1)]
        [InlineData(0, 0, 22050, 128)]
        public void Validator_RejectsInvalidMetadata(int start, int end, int sampleRate, int root)
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            SnesSampleInstrument sample = (SnesSampleInstrument)song.Instruments[0];
            sample.SampleData = SampleDataCodec.Encode(new short[] { 1, 2, 3, 4 });
            sample.SampleRate = sampleRate;
            sample.RootMidiNote = root;
            sample.LoopStart = start;
            sample.LoopEnd = end;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>2 MiB ちょうどを許可し、非ループ時はループ位置を使わない。</summary>
        [Fact]
        public void Validator_AcceptsLimitAndIgnoresDisabledLoopRange()
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            SnesSampleInstrument sample = (SnesSampleInstrument)song.Instruments[0];
            sample.SampleData = SampleDataCodec.Encode(new short[SampleDataCodec.MaximumByteCount / sizeof(short)]);
            sample.Loop = false;
            sample.LoopStart = -1;
            sample.LoopEnd = int.MaxValue;
            SongValidator.Validate(song);
            sample.SampleData = Convert.ToBase64String(new byte[SampleDataCodec.MaximumByteCount + sizeof(short)]);
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }
    }
}
