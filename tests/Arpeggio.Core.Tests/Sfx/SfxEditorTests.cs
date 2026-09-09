using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>定義と生成列の一括保存、コピー隔離、履歴、通常ソングへの移行を検証する。</summary>
    public sealed class SfxEditorTests
    {
        private const int SampleRate = 44100;
        private const string TwoLayerPatch = "{\"tone\":{\"baseFrequencyHz\":880,\"slideSemitonesPerSecond\":-12,\"envelope\":{\"decaySeconds\":0.05}},\"noise\":{\"enabled\":true}}";

        /// <summary>三チップで定義・二声生成列・出自が一保存一履歴となり、開き直しても一致する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void TweakSavesDefinitionAndGeneratedContentAsOneHistoryEntry(ChipKind chip)
        {
            using var fixture = new SfxEditFixture(chip);
            Song published = fixture.Song;
            Track track = published.Tracks[0];
            string before = SongSerializer.Serialize(published);
            string beforeRevision = SfxHash.ComputeRevision(published);
            SfxEditResult result = fixture.Session.Sfx.Tweak(TwoLayerPatch, beforeRevision);
            string after = SongSerializer.Serialize(published);

            Assert.True(result.Changed);
            Assert.False(result.DryRun);
            Assert.True(result.Synchronization.Editable);
            Assert.Equal("tweak", result.Operation);
            Assert.Equal(SfxHash.ComputeRevision(published), result.Revision);
            Assert.Equal(result.Revision, result.CandidateRevision);
            Assert.NotEqual(beforeRevision, result.Revision);
            Assert.Equal(after, File.ReadAllText(fixture.Path));
            Assert.Equal(after, SongSerializer.Serialize(result.Candidate));
            Assert.Same(published, fixture.Song);
            Assert.Same(track, published.Tracks[0]);
            Assert.Equal(1, fixture.Session.History.UndoCount);
            Assert.Equal(2, published.Instruments.Count);
            SfxDefinitionData definition = published.Sfx!.Known!;
            Assert.Equal(880, definition.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(-12, definition.Parameters.Tone.SlideSemitonesPerSecond);
            Assert.Equal("jump", definition.SourcePreset);
            Assert.Equal(uint.MaxValue, definition.LastRandomization!.Seed);
            Assert.Equal(new[] { "tone.baseFrequencyHz" }, definition.LastRandomization.Locks);
            Assert.Equal(81, track.Notes[0].MidiNote);
            Assert.Equal(14, track.Notes[0].DurationTicks);
            Assert.Equal(new[] { 0, -20, -40, -60, -80, -100, -120 }, GetPitchMacro(published).Values);
            Assert.NotNull(result.Generation);
            Assert.Equal(chip == ChipKind.Snes ? 1 : 3, result.Generation.NoiseTrackIndex);

            Assert.True(fixture.Session.Undo());
            Assert.Equal(before, SongSerializer.Serialize(published));
            Assert.Equal(before, File.ReadAllText(fixture.Path));
            Assert.True(fixture.Session.Redo());
            Assert.Equal(after, SongSerializer.Serialize(published));
            fixture.Session.Open(fixture.Path);
            Assert.Equal(after, SongSerializer.Serialize(fixture.Song));
            Assert.True(SfxSynchronization.Inspect(fixture.Song).Editable);
        }

        /// <summary>公開結果のマクロ・ノート・エコーや履歴コピーの変更は保存済み状態へ漏れない。</summary>
        [Fact]
        public void ReturnedCandidatesAndHistorySnapshotsAreIndependent()
        {
            using var fixture = new SfxEditFixture();
            SfxEditResult result = fixture.Session.Sfx.Tweak(TwoLayerPatch);
            string published = SongSerializer.Serialize(fixture.Song);
            string undo = SongSerializer.Serialize(fixture.Session.History.GetUndoSnapshots()[0]);
            result.Candidate.Tracks[0].Notes[0].MidiNote = 60;
            GetPitchMacro(result.Candidate).Values[0] = 100;
            result.Candidate.SnesEcho.FirCoefficients[0] = 0;
            result.Candidate.Sfx = null;
            Song history = fixture.Session.History.GetUndoSnapshots()[0];
            history.Sfx = null;
            GetPitchMacro(history).Values[0] = 200;
            Assert.Equal(published, SongSerializer.Serialize(fixture.Song));
            Assert.Equal(published, File.ReadAllText(fixture.Path));
            Assert.Equal(undo, SongSerializer.Serialize(fixture.Session.History.GetUndoSnapshots()[0]));
            Assert.True(fixture.Session.Undo());
            fixture.Song.Sfx = null;
            Assert.True(fixture.Session.Redo());
            Assert.Equal(published, SongSerializer.Serialize(fixture.Song));
        }

        /// <summary>入力出自の可変ロック一覧を生成候補間で共有しない。</summary>
        [Fact]
        public void CreateCandidateNormalizesAndCopiesProvenance()
        {
            SfxParameters parameters = SfxParameterCatalog.CreateDefaults(ChipKind.GameBoy);
            parameters = parameters with { Tone = parameters.Tone with { BaseFrequencyHz = 440.1234567 } };
            var locks = new List<string> { "tone.baseFrequencyHz" };
            var randomization = new SfxRandomization
            {
                Operation = SfxRandomizationOperation.Mutate, Seed = 1, Strength = 0.1,
                Locks = locks, BaseParametersHash = SfxHash.ComputeParametersHash(parameters, ChipKind.GameBoy)
            };
            SfxSongCompilationResult first = SfxEditor.CreateCandidate(parameters, ChipKind.GameBoy,
                lastRandomization: randomization);
            SfxSongCompilationResult second = SfxEditor.CreateCandidate(parameters, ChipKind.GameBoy,
                lastRandomization: randomization);
            string expected = SongSerializer.Serialize(first.Song);
            locks[0] = "noise.enabled";
            GetPitchMacro(second.Song).Values[0] = 100;
            Assert.Equal(expected, SongSerializer.Serialize(first.Song));
            Assert.Equal(440.123457, first.Song.Sfx!.Known!.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(440.1234567, parameters.Tone.BaseFrequencyHz);
            Assert.True(SfxSynchronization.Inspect(first.Song).Editable);
        }

        /// <summary>無効ノイズの値だけの変更も作成意図として保存し、生成列が同じでも一履歴にする。</summary>
        [Fact]
        public void DisabledLayerParameterChangePersistsWithoutChangingGeneratedContent()
        {
            using var fixture = new SfxEditFixture();
            string before = SongSerializer.Serialize(fixture.Song);
            string generatedHash = SfxHash.ComputeGeneratedHash(fixture.Song);
            string parametersHash = fixture.Song.Sfx!.Known!.ParametersHash;
            SfxEditResult result = fixture.Session.Sfx.Tweak("{\"noise\":{\"envelope\":{\"volume\":5}}}");
            SfxDefinitionData definition = fixture.Song.Sfx!.Known!;
            Assert.True(result.Changed);
            Assert.Equal(generatedHash, SfxHash.ComputeGeneratedHash(fixture.Song));
            Assert.NotEqual(parametersHash, definition.ParametersHash);
            Assert.Equal(5, definition.Parameters.Noise.Envelope.Volume);
            SfxSongCompilationResult generation = Assert.IsType<SfxSongCompilationResult>(result.Generation);
            Assert.Same(generation.Curves.Tone!.PitchMacro, GetPitchMacro(generation.Song));
            Assert.Equal(1, fixture.Session.History.UndoCount);
            Assert.Equal(result.CandidateRevision, SfxHash.ComputeRevision(SongSerializer.Load(fixture.Path)));
            Assert.True(fixture.Session.Undo());
            Assert.Equal(before, SongSerializer.Serialize(fixture.Song));
        }

        /// <summary>全置換は名前・ミュート・追加音色も戻し、Undo は手動編集した音を復元する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void RegenerateReplacesWholeGeneratedRegionAndUndoRestoresManualEdits(ChipKind chip)
        {
            Song original = SfxEditFixture.CreateSong(chip);
            string generatedHash = SfxHash.ComputeGeneratedHash(original);
            original.Title = "保持する曲名";
            original.TempoBpm = 120;
            original.Tracks[1].Muted = true;
            original.Tracks[1].Name = "空トラックの編集";
            original.Tracks[0].Notes[0].Volume = 3;
            Instrument extra = InstrumentJson.Deserialize(InstrumentJson.Serialize(original.Instruments[0]));
            extra.Id = 9;
            original.Instruments.Add(extra);
            using var fixture = new SfxEditFixture(chip, original);
            string manual = SongSerializer.Serialize(fixture.Song);
            SfxEditResult preview = fixture.Session.Sfx.Regenerate(true, dryRun: true);
            Assert.Equal(2, preview.Replacement!.InstrumentCount);
            Assert.Equal(original.Tracks.Count, preview.Replacement.TrackCount);
            Assert.Equal(1, preview.Replacement.NoteCount);
            Assert.Equal(manual, File.ReadAllText(fixture.Path));
            SfxEditResult result = fixture.Session.Sfx.Regenerate(true, preview.Revision);
            Assert.Equal(preview.CandidateRevision, result.Revision);
            Assert.Equal("保持する曲名", fixture.Song.Title);
            Assert.Equal(generatedHash, SfxHash.ComputeGeneratedHash(fixture.Song));
            Assert.True(result.Synchronization.Editable);
            Assert.Equal(1, fixture.Session.History.UndoCount);
            Assert.True(fixture.Session.Undo());
            Assert.Equal(manual, File.ReadAllText(fixture.Path));
            Assert.Equal(manual, SongSerializer.Serialize(fixture.Song));
            Assert.True(fixture.Session.Redo());
            Assert.Equal(result.CandidateRevision, SfxHash.ComputeRevision(fixture.Song));
        }

        /// <summary>両指紋不一致でも明示再生成だけが保存パラメータを音へ反映し、履歴で全て戻せる。</summary>
        [Fact]
        public void RegenerateRepairsBothHashesWithoutDiscardingProvenance()
        {
            Song initial = SfxEditFixture.CreateSong();
            SfxDefinitionData definition = initial.Sfx!.Known!;
            initial.Sfx = new SfxDefinition(definition with
            {
                Parameters = definition.Parameters with { Tone = definition.Parameters.Tone with { BaseFrequencyHz = 880 } }
            });
            initial.Tracks[0].Notes[0].Volume = 4;
            using var fixture = new SfxEditFixture(initial: initial);
            string before = SongSerializer.Serialize(fixture.Song);
            Assert.Equal(2, SfxSynchronization.Inspect(fixture.Song).Reasons.Count);
            fixture.Session.Sfx.Regenerate(true);
            Assert.Equal(81, fixture.Song.Tracks[0].Notes[0].MidiNote);
            Assert.Equal(15, fixture.Song.Tracks[0].Notes[0].Volume);
            Assert.True(SfxSynchronization.Inspect(fixture.Song).Editable);
            Assert.Equal(definition.LastRandomization!.BaseParametersHash,
                fixture.Song.Sfx!.Known!.LastRandomization!.BaseParametersHash);
            Assert.True(fixture.Session.Undo());
            Assert.Equal(before, SongSerializer.Serialize(fixture.Song));
        }

        /// <summary>detach は三チップとも生成 JSON と PCM 全バイトを保持し、Undo/Redo で定義だけが戻る。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void DetachPreservesGeneratedJsonAndEveryPcmByte(ChipKind chip)
        {
            using var fixture = new SfxEditFixture(chip);
            string before = SongSerializer.Serialize(fixture.Song);
            Song plain = SongSerializer.Deserialize(before);
            plain.Sfx = null;
            byte[] audio = RenderBytes(fixture.Song);
            SfxEditResult result = fixture.Session.Sfx.Detach();
            Assert.True(result.Changed);
            Assert.Null(result.Generation);
            Assert.Equal(SfxEditabilityReason.MissingDefinition, result.Synchronization.Reason);
            Assert.Null(fixture.Song.Sfx);
            Assert.Equal(SongSerializer.Serialize(plain), File.ReadAllText(fixture.Path));
            Assert.Equal(audio, RenderBytes(fixture.Song));
            Assert.True(fixture.Session.Undo());
            Assert.Equal(before, SongSerializer.Serialize(fixture.Song));
            Assert.Equal(audio, RenderBytes(fixture.Song));
            Assert.True(fixture.Session.Redo());
            Assert.Null(fixture.Song.Sfx);
            Assert.Equal(audio, RenderBytes(fixture.Song));
        }

        private static Macro GetPitchMacro(Song song)
        {
            return song.Instruments[0] switch
            {
                NesPulseInstrument instrument => instrument.PitchMacro!,
                GbPulseInstrument instrument => instrument.PitchMacro!,
                SnesSampleInstrument instrument => instrument.PitchMacro!,
                _ => throw new InvalidOperationException("トーン音色が必要です。")
            };
        }

        private static byte[] RenderBytes(Song song)
        {
            float[] samples = new SongRenderer(song, new RenderSettings(SampleRate, 1, 0)).RenderAll();
            return MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        }
    }
}
