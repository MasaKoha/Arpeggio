using System;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis.Snes
{
    /// <summary>DSP 設定の追加のみの JSON 互換性と入力境界。</summary>
    public sealed class SnesDspSettingsTests
    {
        /// <summary>新項目なしの JSON は version 1 のまま既定動作で読める。</summary>
        [Fact]
        public void Json_LegacyPropertiesRemainOptional()
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            JsonObject document = JsonNode.Parse(SongSerializer.Serialize(song))!.AsObject();
            JsonObject instrument = document["instruments"]![0]!.AsObject();
            foreach (string name in new[] { "adsrRegisters", "pitchModulation", "noiseEnabled", "noiseRate" })
            {
                instrument.Remove(name);
            }
            document["snesEcho"]!.AsObject().Remove("firCoefficients");
            Song restored = SongSerializer.Deserialize(document.ToJsonString());
            SnesSampleInstrument sample = Assert.IsType<SnesSampleInstrument>(restored.Instruments[0]);
            Assert.Null(sample.AdsrRegisters);
            Assert.False(sample.PitchModulation);
            Assert.False(sample.NoiseEnabled);
            Assert.Equal(31, sample.NoiseRate);
            Assert.Equal(SnesEchoFirPresets.Flat, restored.SnesEcho.FirCoefficients);
            Assert.Equal(1, restored.Version);
        }

        /// <summary>新項目を保存し、BRR キャッシュは JSON に出さない。</summary>
        [Fact]
        public void Json_RoundTripsNewSettingsWithoutCache()
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            SnesSampleInstrument sample = (SnesSampleInstrument)song.Instruments[0];
            sample.AdsrRegisters = new SnesAdsrRegisters(15, 7, 6, 20);
            sample.PitchModulation = true;
            sample.NoiseEnabled = true;
            sample.NoiseRate = 19;
            sample.SampleData = SampleDataCodec.Encode(new short[] { 0, 16000, -16000 });
            song.SnesEcho.FirCoefficients = SnesEchoFirPresets.Wide;
            string json = SongSerializer.Serialize(song);
            Song restored = SongSerializer.Deserialize(json);
            Assert.Equal(json, SongSerializer.Serialize(restored));
            Assert.Equal(sample.AdsrRegisters, ((SnesSampleInstrument)restored.Instruments[0]).AdsrRegisters);
            Assert.DoesNotContain("preparedSample", json);
            Assert.DoesNotContain("decodedSamples", json);
            Assert.Equal(sample.SampleData, ((SnesSampleInstrument)restored.Instruments[0]).SampleData);
        }

        /// <summary>各レジスタの上下限外を拒否する。</summary>
        [Theory]
        [InlineData(-1, 0, 0, 0)]
        [InlineData(16, 0, 0, 0)]
        [InlineData(0, -1, 0, 0)]
        [InlineData(0, 8, 0, 0)]
        [InlineData(0, 0, -1, 0)]
        [InlineData(0, 0, 8, 0)]
        [InlineData(0, 0, 0, -1)]
        [InlineData(0, 0, 0, 32)]
        public void Validator_RejectsInvalidAdsrRegisters(int attack, int decay, int sustainLevel, int sustainRate)
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            ((SnesSampleInstrument)song.Instruments[0]).AdsrRegisters = new SnesAdsrRegisters(attack, decay, sustainLevel, sustainRate);
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>ノイズ速度と FIR の要素数・signed 8 bit 範囲を拒否する。</summary>
        [Fact]
        public void Validator_RejectsInvalidNoiseAndFir()
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            SnesSampleInstrument sample = (SnesSampleInstrument)song.Instruments[0];
            sample.NoiseRate = -1;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            sample.NoiseRate = 32;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            sample.NoiseRate = 0;
            song.SnesEcho.FirCoefficients = null!;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            song.SnesEcho.FirCoefficients = new int[7];
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            song.SnesEcho.FirCoefficients = new int[8];
            song.SnesEcho.FirCoefficients[0] = -129;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            song.SnesEcho.FirCoefficients[0] = 128;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            song.SnesEcho.FirCoefficients[0] = -128;
            SongValidator.Validate(song);
        }

        /// <summary>プリセット取得は別配列になり、別ソングへ編集が漏れない。</summary>
        [Theory]
        [InlineData("Flat")]
        [InlineData("LowPass")]
        [InlineData("HighPass")]
        [InlineData("Wide")]
        public void FirPresets_ReturnIndependentValidCoefficients(string name)
        {
            int[] first = SnesEchoFirPresets.Get(name);
            int[] second = SnesEchoFirPresets.Get(name);
            Assert.Equal(8, first.Length);
            Assert.Equal(first, second);
            Assert.NotSame(first, second);
            Assert.All(first, coefficient => Assert.InRange(coefficient, -128, 127));
        }

        /// <summary>P=4096 は原速、0 と上限は 14 bit の端点になる。</summary>
        [Fact]
        public void Pitch_UsesFourteenBitDspStep()
        {
            Assert.Equal(0, PitchTable.GetSnesPitchRegister(0));
            Assert.Equal(4096, PitchTable.GetSnesPitchRegister(1));
            Assert.Equal(16383, PitchTable.GetSnesPitchRegister(4));
            Assert.Equal(1, PitchTable.GetSnesPitchRegister(0.5 / 4096));
        }
    }
}
