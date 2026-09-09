using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>指紋による状態判定と、読み込み・保存が音を再生成しない契約を検証する。</summary>
    public sealed class SfxSynchronizationTests
    {
        private const int SampleRate = 44100;

        /// <summary>タイトルだけの変更は直接編集を維持し、状態取得で何も変更しない。</summary>
        [Fact]
        public void TitleChangeStaysEditableWithoutMutatingSong()
        {
            Song song = SfxDocumentTestData.CreateSong();
            song.Title = "名前だけ変更";
            string before = SongSerializer.Serialize(song);
            SfxSynchronizationState state = SfxSynchronization.Inspect(song);
            Assert.True(state.Editable);
            Assert.Equal(SfxEditabilityReason.None, state.Reason);
            Assert.Empty(state.Reasons);
            Assert.Same(song.Sfx!.Known!.Parameters, state.Parameters);
            Assert.Null(state.SavedParameters);
            Assert.Equal(before, SongSerializer.Serialize(song));
        }

        /// <summary>音色・ノート以外の生成領域も編集検出し、ロードで復元し直さない。</summary>
        [Fact]
        public void GeneratedContentChangesArePreserved()
        {
            foreach (Action<Song> edit in GeneratedEdits())
            {
                Song song = SfxDocumentTestData.CreateSong();
                edit(song);
                string changed = SongSerializer.Serialize(song);
                Song restored = SongSerializer.Deserialize(changed);
                SfxSynchronizationState state = SfxSynchronization.Inspect(restored);
                Assert.False(state.Editable);
                Assert.Equal(SfxEditabilityReason.GeneratedContentChanged, state.Reason);
                Assert.Equal(SfxEditabilityReason.GeneratedContentChanged, Assert.Single(state.Reasons));
                Assert.Null(state.Parameters);
                Assert.Equal(song.Sfx!.Known!.Parameters, state.SavedParameters);
                Assert.Equal(changed, SongSerializer.Serialize(restored));
            }
        }

        /// <summary>パラメータだけの JSON 手修正は生成列へ反映せず、保存意図として表示する。</summary>
        [Fact]
        public void SavedParameterEditDoesNotRegenerateNotesOrMacros()
        {
            Song original = SfxDocumentTestData.CreateSong();
            JsonObject document = SfxDocumentTestData.ReadDocument(original);
            SfxDocumentTestData.Definition(document)["parameters"]!["tone"]!["baseFrequencyHz"] = 880;
            Song restored = SongSerializer.Deserialize(document.ToJsonString());
            SfxSynchronizationState state = SfxSynchronization.Inspect(restored);
            Assert.False(state.Editable);
            Assert.Equal(SfxEditabilityReason.SavedParametersChanged, state.Reason);
            Assert.Equal(SfxEditabilityReason.SavedParametersChanged, Assert.Single(state.Reasons));
            Assert.Null(state.Parameters);
            Assert.Equal(880, state.SavedParameters!.Tone.BaseFrequencyHz);
            Assert.Equal(SfxHash.ComputeGeneratedHash(original), SfxHash.ComputeGeneratedHash(restored));
            Assert.Equal(Render(original), Render(restored));
        }

        /// <summary>両方の不一致を診断し、保存後も主理由と生成列を維持する。</summary>
        [Fact]
        public void ReportsBothHashMismatches()
        {
            Song song = SfxDocumentTestData.CreateSong();
            SfxDefinitionData data = song.Sfx!.Known!;
            song.Sfx = new SfxDefinition(data with
            {
                Parameters = data.Parameters with { Tone = data.Parameters.Tone with { BaseFrequencyHz = 880 } }
            });
            song.Tracks[0].Notes[0].MidiNote++;
            Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            SfxSynchronizationState state = SfxSynchronization.Inspect(restored);
            Assert.Equal(SfxEditabilityReason.SavedParametersChanged, state.Reason);
            Assert.Equal(new[] { SfxEditabilityReason.SavedParametersChanged, SfxEditabilityReason.GeneratedContentChanged }, state.Reasons);
            SfxDefinitionData restoredData = Assert.IsType<SfxDefinitionData>(restored.Sfx!.Known);
            Assert.Equal(data.ParametersHash, restoredData.ParametersHash);
            Assert.Equal(data.GeneratedHash, restoredData.GeneratedHash);
            Assert.Equal(Render(song), Render(restored));
        }

        /// <summary>SNES の既定音色割り当てと生成領域の配列順も指紋に含める。</summary>
        [Fact]
        public void DefaultInstrumentAndArrayOrderAreGeneratedContent()
        {
            Song song = SfxDocumentTestData.CreateSong(ChipKind.Snes);
            song.Tracks[0].DefaultInstrumentId = 1;
            Assert.Equal(SfxEditabilityReason.GeneratedContentChanged, SfxSynchronization.Inspect(song).Reason);
            Song nes = SfxPresetFactory.Create(ChipKind.Nes, SfxPresetKind.Hit);
            nes.Sfx = new SfxDefinition(SfxDocumentTestData.CreateDefinition(nes));
            nes.Instruments.Reverse();
            Assert.Equal(SfxEditabilityReason.GeneratedContentChanged, SfxSynchronization.Inspect(nes).Reason);
        }

        /// <summary>既知版・未知版の定義を保存しても従来24プリセットの PCM は変わらない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void DefinitionsNeverAffectLegacyPresetAudio(ChipKind chip)
        {
            foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
            {
                Song song = SfxPresetFactory.Create(chip, preset.Kind);
                string legacyJson = SongSerializer.Serialize(song);
                float[] expected = Render(song);
                song.Sfx = new SfxDefinition(SfxDocumentTestData.CreateDefinition(song));
                Song restored = SongSerializer.Deserialize(SongSerializer.Serialize(song));
                Assert.Equal(expected, Render(restored));
                JsonObject unknown = SfxDocumentTestData.ReadDocument(restored);
                unknown["sfx"] = JsonNode.Parse("{\"schemaVersion\":99,\"parameters\":\"future\"}");
                Song opaque = SongSerializer.Deserialize(unknown.ToJsonString());
                Assert.Equal(expected, Render(SongSerializer.Deserialize(SongSerializer.Serialize(opaque))));
                opaque.Sfx = null;
                Assert.Equal(legacyJson, SongSerializer.Serialize(opaque));
            }
        }

        private static IEnumerable<Action<Song>> GeneratedEdits()
        {
            yield return song => ((NesPulseInstrument)song.Instruments[0]).PitchMacro = new Macro { Values = new[] { 100 } };
            yield return song => song.Tracks[0].Notes[0].MidiNote++;
            yield return song => song.Tracks[0].Notes[0].Effects = Array.Empty<NoteEffect>();
            yield return song => song.Instruments[0].Name = "edited";
            yield return song => song.Tracks[0].Name = "edited";
            yield return song => song.Tracks[0].Muted = true;
            yield return song => song.Tracks[0].Pan = 0.5;
            yield return song => song.Tracks[1].Muted = true;
            yield return song => song.TempoBpm++;
            yield return song => song.LengthTicks++;
            yield return song => song.LoopStartTick++;
            yield return song => song.SnesEcho.DelayMilliseconds = 16;
            yield return song => song.SnesEcho.FirCoefficients[0] = 126;
        }

        private static float[] Render(Song song)
        {
            return new SongRenderer(song, new RenderSettings(SampleRate, 1, 0)).RenderAll();
        }
    }
}
