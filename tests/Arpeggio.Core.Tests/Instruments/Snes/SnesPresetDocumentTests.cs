using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Session;
using Arpeggio.Core.Render;
using Arpeggio.Core.Synthesis.Snes;
using Xunit;

namespace Arpeggio.Core.Tests.Instruments.Snes
{
    /// <summary>プリセットの既定値・保存・入力拒否と八トラック割り当てを検証する。</summary>
    public sealed class SnesPresetDocumentTests
    {
        /// <summary>明示値は JSON の項目順によらず推奨値より優先し、null レジスタは秒指定に戻す。</summary>
        [Theory]
        [InlineData("{\"kind\":\"SnesSample\",\"preset\":\"piano\",\"loop\":true,\"rootMidiNote\":72,\"echoSend\":0.6,\"adsrRegisters\":null}")]
        [InlineData("{\"kind\":\"SnesSample\",\"loop\":true,\"rootMidiNote\":72,\"echoSend\":0.6,\"adsrRegisters\":null,\"preset\":\"piano\"}")]
        public void ExplicitValues_AreIndependentOfJsonOrder(string json)
        {
            SnesSampleInstrument instrument = Assert.IsType<SnesSampleInstrument>(InstrumentJson.Deserialize(json));
            Assert.Equal("piano", instrument.Preset);
            Assert.True(instrument.Loop);
            Assert.Equal(72, instrument.RootMidiNote);
            Assert.Equal(0.6, instrument.EchoSend);
            Assert.Null(instrument.AdsrRegisters);
            Assert.Equal(SnesInstrumentBank.SampleRate, instrument.SampleRate);
        }

        /// <summary>名前だけの入力で推奨値が有効になり、保存にサンプル配列を含めない。</summary>
        [Fact]
        public void PresetOnlyJson_UsesRecommendationsAndRoundTripsWithoutSamples()
        {
            SnesSampleInstrument instrument = Assert.IsType<SnesSampleInstrument>(InstrumentJson.Deserialize("{\"kind\":\"SnesSample\",\"preset\":\"strings\"}"));
            SnesInstrumentPreset preset = SnesInstrumentCatalog.Get("strings");
            Assert.Equal(preset.AdsrRegisters, instrument.AdsrRegisters);
            Assert.Equal(preset.RootMidiNote, instrument.RootMidiNote);
            Assert.Equal(preset.EchoSend, instrument.EchoSend);
            Song song = SongFactory.Create(ChipKind.Snes);
            song.Instruments[0] = instrument;
            string json = SongSerializer.Serialize(song);
            SnesSampleInstrument restored = Assert.IsType<SnesSampleInstrument>(SongSerializer.Deserialize(json).Instruments[0]);
            Assert.Null(restored.SampleData);
            Assert.Equal(0, restored.SampleCount);
            Assert.Equal("strings", restored.Preset);
            Assert.DoesNotContain("prepared", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Render(instrument), Render(restored));
        }

        /// <summary>未知名・大文字・空文字をドキュメントエラーとして拒否する。</summary>
        [Theory]
        [InlineData("unknown")]
        [InlineData("Strings")]
        [InlineData("")]
        public void UnknownPreset_IsRejected(string preset)
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            song.Instruments[0] = new SnesSampleInstrument { Preset = preset };
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
            Assert.Throws<ArgumentException>(() => SnesInstrumentBank.Build(preset));
        }

        /// <summary>二重指定はエラーだが、検証を通さないボイスでは埋め込み素材を優先する。</summary>
        [Fact]
        public void EmbeddedSample_TakesPlaybackPriorityButConflictIsInvalid()
        {
            const int SampleRate = 32000;
            float[] samples = new float[128];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (float)(0.5 * Math.Sin(2 * Math.PI * index / samples.Length));
            }
            string data = SampleDataCodec.Encode(SampleDataCodec.FromFloat(samples));
            SnesSampleInstrument embedded = new SnesSampleInstrument { SampleData = data, SampleRate = SampleRate };
            SnesSampleInstrument conflict = new SnesSampleInstrument
            {
                Preset = "strings", SampleData = data, SampleRate = SampleRate, RootMidiNote = 60,
                AdsrRegisters = null, EchoSend = 0
            };
            Assert.Equal(Render(embedded), Render(conflict));
            Song song = SongFactory.Create(ChipKind.Snes);
            song.Instruments[0] = conflict;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>ループ範囲・サンプルレート・ルート音をプリセットでも検証する。</summary>
        [Theory]
        [InlineData(-1, 0, 32000, 60)]
        [InlineData(128, 128, 32000, 60)]
        [InlineData(0, 129, 32000, 60)]
        [InlineData(0, 0, 0, 60)]
        [InlineData(0, 0, 32000, 128)]
        public void InvalidSampleMetadata_IsRejected(int start, int end, int rate, int root)
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            song.Instruments[0] = new SnesSampleInstrument { Preset = "strings", LoopStart = start, LoopEnd = end, SampleRate = rate, RootMidiNote = root };
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>全編成が八音色を保存し、名前変更後も既定の割り当てを維持する。</summary>
        [Theory]
        [InlineData(SnesBankKind.Orchestral, "strings,brass,flute,choir,bass,kick,snare,hat")]
        [InlineData(SnesBankKind.Band, "lead,organ,pluck,bass,piano,kick,snare,hat")]
        [InlineData(SnesBankKind.Chip, "lead,lead,bass,organ,bell,kick,snare,hat")]
        public void Bank_AssignsEightIndependentInstruments(SnesBankKind bank, string expectedPresets)
        {
            Song song = SongFactory.Create(ChipKind.Snes, bank: bank);
            Assert.Equal(8, song.Tracks.Count);
            Assert.Equal(expectedPresets.Split(','), song.Tracks.Select(track => track.Name));
            Assert.Equal(expectedPresets.Split(','), song.Instruments.Cast<SnesSampleInstrument>().Select(instrument => instrument.Preset));
            for (int index = 0; index < song.Tracks.Count; index++)
            {
                song.Tracks[index].Name = "renamed";
                Assert.Equal(index + 1, DefaultInstrumentResolver.Resolve(song, index, null));
                Assert.Equal(1, DefaultInstrumentResolver.Resolve(song, index, 1));
            }
            Assert.Equal(SongSerializer.Serialize(song), SongSerializer.Serialize(SongSerializer.Deserialize(SongSerializer.Serialize(song))));
        }

        /// <summary>バンク未指定時は既存の lead 一音色、別チップへの指定は拒否する。</summary>
        [Fact]
        public void NoBank_PreservesLegacySongAndOtherChipsRejectBanks()
        {
            Song song = SongFactory.Create(ChipKind.Snes);
            SnesSampleInstrument instrument = Assert.IsType<SnesSampleInstrument>(Assert.Single(song.Instruments));
            Assert.Equal("lead", instrument.Name);
            Assert.Null(instrument.Preset);
            Assert.All(song.Tracks, track => Assert.Null(track.DefaultInstrumentId));
            Assert.Throws<SongValidationException>(() => SongFactory.Create(ChipKind.Nes, bank: SnesBankKind.Chip));
            Assert.Throws<SongValidationException>(() => SongFactory.Create(ChipKind.GameBoy, bank: SnesBankKind.Band));
        }

        /// <summary>プリセットの音域警告にも指定したルート音を使う。</summary>
        [Fact]
        public void PitchWarning_UsesPresetRootNote()
        {
            const int RootMidiNote = 48;
            const int RequestedMidiNote = 84;
            const int SongLength = 48;
            Song song = SongFactory.Create(ChipKind.Snes, lengthTicks: SongLength);
            song.Instruments[0] = new SnesSampleInstrument { Preset = "strings", RootMidiNote = RootMidiNote };
            song.Tracks[0].Notes.Add(new Note { MidiNote = RequestedMidiNote, DurationTicks = SongLength });
            SongRenderer renderer = new SongRenderer(song, new RenderSettings(32000, 1, 0));
            renderer.RenderAll();
            RenderWarning warning = Assert.Single(renderer.Report.Warnings);
            Assert.Equal(RenderWarningKind.PitchClamped, warning.Kind);
            Assert.InRange(warning.ActualValue, 71.9, 72);
        }

        private static float[] Render(SnesSampleInstrument instrument)
        {
            SnesVoiceSynthesizer voice = new SnesVoiceSynthesizer(32000);
            voice.NoteOn(60, 15, instrument, ReadOnlySpan<NoteEffect>.Empty);
            float[] samples = new float[32000];
            voice.Render(samples);
            return samples;
        }
    }
}
