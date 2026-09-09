using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Sfx
{
    /// <summary>NES / GB の生成領域・独立包絡・入力隔離と保存契約を検証する。</summary>
    public sealed class SfxSongCompilerTests
    {
        /// <summary>無効レイヤーを省略してもトラックと音色 ID を詰めず、各ノートにゼロ保持を確保する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, true, false, 5)]
        [InlineData(ChipKind.Nes, false, true, 5)]
        [InlineData(ChipKind.Nes, true, true, 5)]
        [InlineData(ChipKind.GameBoy, true, false, 4)]
        [InlineData(ChipKind.GameBoy, false, true, 4)]
        [InlineData(ChipKind.GameBoy, true, true, 4)]
        public void Compile_PreservesFixedLayoutAndIndependentEnds(ChipKind chip, bool toneEnabled, bool noiseEnabled, int trackCount)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with { Enabled = toneEnabled, SlideSemitonesPerSecond = -12 },
                Noise = parameters.Noise with
                {
                    Enabled = noiseEnabled,
                    Envelope = new SfxEnvelopeParameters { SustainSeconds = 1.0 / 60, DecaySeconds = 4.0 / 60 }
                }
            };
            SfxSongCompilationResult result = SfxSongCompiler.Compile(parameters, chip, "生成テスト");
            Song song = result.Song;
            SongValidator.Validate(song);
            Assert.Equal("生成テスト", song.Title);
            Assert.Equal(1, song.Version);
            Assert.Equal(150, song.TempoBpm);
            Assert.Equal(48, song.TicksPerBeat);
            Assert.Equal(0, song.LoopStartTick);
            Assert.Equal(noiseEnabled ? 12 : 8, song.LengthTicks);
            Assert.Equal(trackCount, song.Tracks.Count);
            Assert.Equal(toneEnabled ? (int?)0 : null, result.ToneTrackIndex);
            Assert.Equal(noiseEnabled ? (int?)3 : null, result.NoiseTrackIndex);
            Assert.Null(song.Sfx);
            Song layout = SongFactory.Create(chip);
            for (int index = 0; index < song.Tracks.Count; index++)
            {
                Track track = song.Tracks[index];
                Assert.Equal(layout.Tracks[index].Channel, track.Channel);
                Assert.Equal(layout.Tracks[index].ChannelIndex, track.ChannelIndex);
                Assert.Equal(layout.Tracks[index].Name, track.Name);
                Assert.False(track.Muted);
                Assert.Equal(0, track.Pan);
                Assert.Null(track.DefaultInstrumentId);
                bool occupied = (index == 0 && toneEnabled) || (index == 3 && noiseEnabled);
                Assert.Equal(occupied ? 1 : 0, track.Notes.Count);
            }
            Assert.Equal(new[] { toneEnabled ? 1 : 0, noiseEnabled ? 2 : 0 }.Where(identifier => identifier != 0),
                song.Instruments.Select(instrument => instrument.Id));
            if (toneEnabled)
            {
                AssertLayer(song, 0, 1, "sfx-tone", 69, 8, new[] { 12, 8, 4, 0 });
                Assert.Equal(new[] { 0, -20, -40, -60 }, SfxCompilationTestData.Pitch(song.Instruments[0]).Values);
                Assert.Equal(new[] { 2, 2, 2, 2 }, SfxCompilationTestData.Duty(song.Instruments[0]).Values);
            }
            if (noiseEnabled)
            {
                AssertLayer(song, 3, 2, "sfx-noise", chip == ChipKind.Nes ? 12 : 96, 12, new[] { 12, 12, 9, 6, 3, 0 });
            }
            string serialized = SongSerializer.Serialize(song);
            Assert.Equal(serialized, SongSerializer.Serialize(SongSerializer.Deserialize(serialized)));
        }

        /// <summary>GB ハードウェア包絡は固定し、自由包絡をマクロだけから入力する。</summary>
        [Fact]
        public void Compile_FixesGameBoyHardwareEnvelope()
        {
            Song song = SfxSongCompiler.Compile(SfxCompilationTestData.ShortParameters(ChipKind.GameBoy), ChipKind.GameBoy).Song;
            var pulse = Assert.IsType<GbPulseInstrument>(Assert.Single(song.Instruments));
            Assert.Equal(15, pulse.InitialVolume);
            Assert.Equal(0, pulse.EnvelopeStepFrames);
            Assert.False(pulse.EnvelopeIncreasing);
            Assert.Equal(DutyCycle.Percent25, pulse.Duty);
            Assert.NotNull(pulse.ArpeggioMacro);
            Assert.Equal(new[] { 0, 0, 0, 0 }, pulse.ArpeggioMacro.Values);
            Assert.Equal(-1, pulse.ArpeggioMacro.LoopIndex);
        }

        /// <summary>生成結果を変更しても入力と別の生成結果へ伝播せず、正規化前の入力も保持する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        public void Compile_IsDeterministicAndIsolatesMutableData(ChipKind chip)
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(chip);
            parameters = parameters with
            {
                Tone = parameters.Tone with { SlideSemitonesPerSecond = -12.1234567 },
                Noise = parameters.Noise with { Enabled = true }
            };
            SfxSongCompilationResult first = SfxSongCompiler.Compile(parameters, chip);
            SfxSongCompilationResult second = SfxSongCompiler.Compile(parameters, chip);
            string expected = SongSerializer.Serialize(second.Song);
            Assert.Equal(expected, SongSerializer.Serialize(first.Song));
            Assert.Equal(first.Warnings, second.Warnings);
            Assert.Equal(first.TonePitchFrames, second.TonePitchFrames);
            Assert.NotSame(SfxCompilationTestData.Volume(first.Song.Instruments[0]), SfxCompilationTestData.Volume(first.Song.Instruments[1]));
            SfxCompilationTestData.Volume(first.Song.Instruments[0]).Values[0] = 0;
            SfxCompilationTestData.Pitch(first.Song.Instruments[1]).Values[0] = 100;
            SfxCompilationTestData.Duty(first.Song.Instruments[0]).Values[0] = 4;
            first.Song.Tracks[0].Notes[0].MidiNote = 60;
            Assert.Equal(expected, SongSerializer.Serialize(second.Song));
            Assert.Equal(-12.1234567, parameters.Tone.SlideSemitonesPerSecond);
            Assert.Equal(-12.123457, first.Curves.Parameters.Tone.SlideSemitonesPerSecond);
        }

        /// <summary>全 OFF と無効レイヤーの不正値も Song を返す前に拒否する。</summary>
        [Fact]
        public void Compile_ValidatesDisabledLayersAndRejectsAllOff()
        {
            SfxParameters parameters = SfxCompilationTestData.ShortParameters(ChipKind.Nes);
            Assert.Throws<SfxParameterException>(() => SfxSongCompiler.Compile(parameters with
            {
                Tone = parameters.Tone with { Enabled = false }
            }, ChipKind.Nes));
            Assert.Throws<SfxParameterException>(() => SfxSongCompiler.Compile(parameters with
            {
                Noise = parameters.Noise with { Envelope = parameters.Noise.Envelope with { Volume = 16 } }
            }, ChipKind.Nes));
        }

        private static void AssertLayer(Song song, int trackIndex, int identifier, string name,
            int midiNote, int duration, int[] volumes)
        {
            Note note = Assert.Single(song.Tracks[trackIndex].Notes);
            Assert.Equal(0, note.Tick);
            Assert.Equal(identifier, note.InstrumentId);
            Assert.Equal(midiNote, note.MidiNote);
            Assert.Equal(duration, note.DurationTicks);
            Assert.Equal(15, note.Volume);
            Assert.Empty(note.Effects);
            Instrument instrument = Assert.Single(song.Instruments, candidate => candidate.Id == identifier);
            Assert.Equal(name, instrument.Name);
            Assert.Equal(volumes, SfxCompilationTestData.Volume(instrument).Values);
            Assert.Equal(-1, SfxCompilationTestData.Volume(instrument).LoopIndex);
            Assert.Equal(-1, SfxCompilationTestData.Pitch(instrument).LoopIndex);
        }
    }
}
