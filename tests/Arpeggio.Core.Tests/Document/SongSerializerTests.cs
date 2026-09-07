using System;
using System.Collections.Generic;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Xunit;

namespace Arpeggio.Core.Tests.Document
{
    /// <summary>正規 JSON と多態音色の永続化を検証する。</summary>
    public sealed class SongSerializerTests
    {
        /// <summary>全チップ・全音色が UTF-8 バイト列を維持して往復する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void RoundTripPreservesCanonicalUtf8Bytes(ChipKind chip)
        {
            Song song = CreateSongWithAllInstruments(chip);
            string first = SongSerializer.Serialize(song);
            string second = SongSerializer.Serialize(SongSerializer.Deserialize(first));
            Assert.Equal(Encoding.UTF8.GetBytes(first), Encoding.UTF8.GetBytes(second));
            Assert.Contains("\"pitchMacro\": null", first);
            Assert.Contains("\n  \"version\": 1,", first);
            Assert.Equal(song.Instruments.Count, SongSerializer.Deserialize(first).Instruments.Count);
        }

        /// <summary>正規出力のトップレベルと音色キー順が設計順になる。</summary>
        [Fact]
        public void CanonicalPropertyOrderMatchesDesign()
        {
            string serialized = SongSerializer.Serialize(SongFactory.Create(ChipKind.Nes));
            AssertOrdered(serialized, "\"version\"", "\"title\"", "\"chip\"", "\"tempoBpm\"", "\"ticksPerBeat\"", "\"lengthTicks\"", "\"loopStartTick\"", "\"instruments\"", "\"tracks\"", "\"snesEcho\"");
            AssertOrdered(serialized, "\"id\"", "\"name\"", "\"kind\"", "\"duty\"", "\"volumeMacro\"", "\"arpeggioMacro\"", "\"pitchMacro\"", "\"dutyMacro\"");
        }

        /// <summary>整数の enum と型判別子を受け入れ、文字列で再保存する。</summary>
        [Fact]
        public void AcceptsIntegerEnumsAndInstrumentDiscriminator()
        {
            string serialized = SongSerializer.Serialize(SongFactory.Create(ChipKind.Nes));
            string numeric = serialized.Replace("\"chip\": \"Nes\"", "\"chip\": 1", StringComparison.Ordinal)
                .Replace("\"kind\": \"NesPulse\"", "\"kind\": 1", StringComparison.Ordinal)
                .Replace("\"duty\": \"Percent50\"", "\"duty\": 3", StringComparison.Ordinal)
                .Replace("\"channel\": \"Pulse\"", "\"channel\": 1", StringComparison.Ordinal);
            Assert.Equal(serialized, SongSerializer.Serialize(SongSerializer.Deserialize(numeric)));
        }

        /// <summary>ノート効果とマクロの値・ループ位置を保存する。</summary>
        [Fact]
        public void PreservesEffectAndMacroValues()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 7, 0 }, LoopIndex = 1 };
            song.Tracks[0].Notes.Add(new Note { Effects = new[] { new NoteEffect(NoteEffectKind.PitchSlide, -4) } });
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            var restoredInstrument = Assert.IsType<NesPulseInstrument>(restored.Instruments[0]);
            Assert.NotNull(restoredInstrument.VolumeMacro);
            Assert.Equal(new[] { 15, 7, 0 }, restoredInstrument.VolumeMacro.Values);
            Assert.Equal(1, restoredInstrument.VolumeMacro.LoopIndex);
            Assert.Equal(new NoteEffect(NoteEffectKind.PitchSlide, -4), Assert.Single(restored.Tracks[0].Notes).Effects[0]);
        }

        /// <summary>破損 JSON と未対応バージョンを統一例外で拒否する。</summary>
        [Theory]
        [InlineData("{}")]
        [InlineData("null")]
        [InlineData("{\"version\": 2}")]
        [InlineData("{\"version\": 1, \"instruments\": [null]}")]
        [InlineData("{\"version\": 1, \"instruments\": [{\"kind\": \"Unknown\"}]}")]
        [InlineData("{")]
        public void RejectsMalformedJson(string serialized)
        {
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(serialized));
        }

        private static Song CreateSongWithAllInstruments(ChipKind chip)
        {
            Song song = SongFactory.Create(chip);
            var instruments = new List<Instrument>();
            switch (chip)
            {
                case ChipKind.Nes:
                    instruments.Add(new NesPulseInstrument());
                    instruments.Add(new NesTriangleInstrument());
                    instruments.Add(new NesNoiseInstrument());
                    instruments.Add(new NesDpcmInstrument());
                    break;
                case ChipKind.GameBoy:
                    instruments.Add(new GbPulseInstrument());
                    instruments.Add(new GbWaveInstrument());
                    instruments.Add(new GbNoiseInstrument());
                    break;
                case ChipKind.Snes:
                    instruments.Add(new SnesSampleInstrument { Envelope = new AdsrEnvelope(0.01, 0.02, 0.75, 0.1) });
                    song.SnesEcho = new SnesEchoSettings { DelayMilliseconds = 32, Feedback = 0.5, Volume = 0.25 };
                    break;
            }
            for (int index = 0; index < instruments.Count; index++)
            {
                instruments[index].Id = index + 1;
                instruments[index].Name = "音色" + index;
            }
            song.Instruments = instruments;
            return song;
        }

        private static void AssertOrdered(string value, params string[] keys)
        {
            int previous = -1;
            foreach (string key in keys)
            {
                int current = value.IndexOf(key, StringComparison.Ordinal);
                Assert.True(current > previous, key);
                previous = current;
            }
        }
    }
}
