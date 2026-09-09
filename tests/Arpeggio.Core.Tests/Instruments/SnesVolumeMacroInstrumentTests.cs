using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Session;
using Xunit;

namespace Arpeggio.Core.Tests.Instruments
{
    /// <summary>SNES optional 音量マクロの保存互換・検証・プリセット保持を検証する。</summary>
    public sealed class SnesVolumeMacroInstrumentTests
    {
        /// <summary>旧音色の全 JSON バイトは未指定と明示 null で不変、新マクロは末尾へ追加する。</summary>
        [Fact]
        public void JsonPreservesLegacyBytesAndAppendsVolumeMacro()
        {
            const string LegacyJson = "{\n  \"kind\": \"SnesSample\",\n  \"id\": 1,\n  \"name\": \"\",\n  \"waveform\": \"Sine\",\n  \"loop\": true,\n  \"envelope\": {\n    \"attackSeconds\": 0,\n    \"decaySeconds\": 0,\n    \"sustainLevel\": 1,\n    \"releaseSeconds\": 0.05\n  },\n  \"echoSend\": 0,\n  \"pan\": 0,\n  \"arpeggioMacro\": null,\n  \"pitchMacro\": null,\n  \"sampleData\": null,\n  \"sampleRate\": 44100,\n  \"rootMidiNote\": 60,\n  \"loopStart\": 0,\n  \"loopEnd\": 0,\n  \"adsrRegisters\": null,\n  \"pitchModulation\": false,\n  \"noiseEnabled\": false,\n  \"noiseRate\": 31,\n  \"preset\": null\n}";
            var instrument = new SnesSampleInstrument();
            Assert.Equal(Encoding.UTF8.GetBytes(LegacyJson), Encoding.UTF8.GetBytes(InstrumentJson.Serialize(instrument)));
            Assert.Equal(LegacyJson, InstrumentJson.Serialize(InstrumentJson.Deserialize(LegacyJson.Replace("\n}", ",\n  \"volumeMacro\": null\n}", StringComparison.Ordinal))));
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 12, 8, 4, 0 }, LoopIndex = 1 };
            string json = InstrumentJson.Serialize(instrument);
            JsonObject document = JsonNode.Parse(json)!.AsObject();
            Assert.Equal("volumeMacro", document.Last().Key);
            var copy = Assert.IsType<SnesSampleInstrument>(InstrumentJson.Deserialize(json));
            Assert.NotNull(copy.VolumeMacro);
            Assert.Equal(instrument.VolumeMacro.Values, copy.VolumeMacro.Values);
            Assert.Equal(1, copy.VolumeMacro.LoopIndex);
            Assert.NotSame(instrument.VolumeMacro.Values, copy.VolumeMacro.Values);
            Song song = SongFactory.Create(ChipKind.Snes);
            song.Instruments[0] = copy;
            string saved = SongSerializer.Serialize(song);
            Assert.Contains("\"version\": 1", saved);
            Assert.Equal(saved, SongSerializer.Serialize(SongSerializer.Deserialize(saved)));
        }

        /// <summary>範囲外音量・null 配列・不正ループは読み込み時に拒否する。</summary>
        [Theory]
        [InlineData("{\"values\":[-1],\"loopIndex\":-1}")]
        [InlineData("{\"values\":[16],\"loopIndex\":-1}")]
        [InlineData("{\"values\":[15],\"loopIndex\":1}")]
        [InlineData("{\"values\":[0],\"loopIndex\":-2}")]
        [InlineData("{\"values\":[],\"loopIndex\":0}")]
        [InlineData("{\"values\":null,\"loopIndex\":-1}")]
        public void JsonRejectsInvalidVolumeMacros(string macroJson)
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            JsonObject document = JsonNode.Parse(SongSerializer.Serialize(song))!.AsObject();
            document["instruments"]![0]!["volumeMacro"] = JsonNode.Parse(macroJson);
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(document.ToJsonString()));
            song.Instruments[0] = InstrumentJson.Deserialize(document["instruments"]![0]!.ToJsonString());
            Assert.Throws<SongValidationException>(() => SongSerializer.Serialize(song));
        }

        /// <summary>全プリセットの推奨値再適用でもマクロ参照と値・ループ位置を保持する。</summary>
        [Fact]
        public void ApplyPresetPreservesVolumeAlongsideExistingMacros()
        {
            var volume = new Macro { Values = new[] { 12, 8, 4, 0 }, LoopIndex = 1 };
            var pitch = new Macro { Values = new[] { 0, 100 } };
            var arpeggio = new Macro { Values = new[] { 0, 7 } };
            var instrument = new SnesSampleInstrument { VolumeMacro = volume, PitchMacro = pitch, ArpeggioMacro = arpeggio };
            foreach (SnesInstrumentPreset preset in SnesInstrumentCatalog.All)
            {
                instrument.ApplyPreset(preset.Name);
                Assert.Same(volume, instrument.VolumeMacro);
                Assert.Same(pitch, instrument.PitchMacro);
                Assert.Same(arpeggio, instrument.ArpeggioMacro);
                Assert.Equal(new[] { 12, 8, 4, 0 }, volume.Values);
                Assert.Equal(1, volume.LoopIndex);
                Assert.Equal(preset.AdsrRegisters, instrument.AdsrRegisters);
            }
        }
    }
}
