using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Xunit;

namespace Arpeggio.Core.Tests.Document
{
    /// <summary>読み書き境界で拒否すべき不正データを検証する。</summary>
    public sealed class SongValidatorTests
    {
        /// <summary>全チップの空ソングが正しいチャンネル構成を持つ。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 5)]
        [InlineData(ChipKind.GameBoy, 4)]
        [InlineData(ChipKind.Snes, 8)]
        public void FactoryCreatesValidLayout(ChipKind chip, int channelCount)
        {
            Song song = SongFactory.Create(chip);
            SongValidator.Validate(song);
            Assert.Equal(channelCount, song.Tracks.Count);
            Assert.Single(song.Instruments);
        }

        /// <summary>ノートの時刻・長さ・音量・MIDI 番号・参照を検証する。</summary>
        [Theory]
        [InlineData(-1, 48, 60, 15, 1)]
        [InlineData(768, 48, 60, 15, 1)]
        [InlineData(0, 0, 60, 15, 1)]
        [InlineData(750, 48, 60, 15, 1)]
        [InlineData(1, int.MaxValue, 60, 15, 1)]
        [InlineData(0, 48, -1, 15, 1)]
        [InlineData(0, 48, 128, 15, 1)]
        [InlineData(0, 48, 60, -1, 1)]
        [InlineData(0, 48, 60, 16, 1)]
        [InlineData(0, 48, 60, 15, 2)]
        public void RejectsInvalidNote(int tick, int duration, int midiNote, int volume, int instrumentId)
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[0].Notes.Add(new Note { Tick = tick, DurationTicks = duration, MidiNote = midiNote, Volume = volume, InstrumentId = instrumentId });
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>同時発音・交差・降順を拒否する。</summary>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 24)]
        [InlineData(48, 0)]
        public void RejectsOverlappingOrUnsortedNotes(int firstTick, int secondTick)
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[0].Notes.Add(new Note { Tick = firstTick });
            song.Tracks[0].Notes.Add(new Note { Tick = secondTick });
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>隣接するノートとチップ音域外の MIDI ノートはデータとして有効。</summary>
        [Fact]
        public void AllowsAdjacentNotesAndHardwareOutOfRangePitch()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            song.Tracks[0].Notes.Add(new Note { MidiNote = 0 });
            song.Tracks[0].Notes.Add(new Note { Tick = 48, MidiNote = 127 });
            SongValidator.Validate(song);
        }

        /// <summary>ソング全体・チャンネル・音色の整合性を拒否する。</summary>
        [Fact]
        public void RejectsSongAndLayoutViolations()
        {
            AssertInvalid(song => song.Version = 2);
            AssertInvalid(song => song.TicksPerBeat = 96);
            AssertInvalid(song => song.TempoBpm = 0);
            AssertInvalid(song => song.LengthTicks = 0);
            AssertInvalid(song => song.LoopStartTick = song.LengthTicks);
            AssertInvalid(song => song.Chip = ChipKind.None);
            AssertInvalid(song => song.Tracks.RemoveAt(0));
            AssertInvalid(song => song.Tracks[0].Channel = ChannelKind.Triangle);
            AssertInvalid(song => song.Tracks[1].ChannelIndex = 0);
            AssertInvalid(song => song.Tracks[0].Pan = double.NaN);
            AssertInvalid(song => song.Instruments.Add(new NesPulseInstrument { Id = 1 }));
            AssertInvalid(song => song.Instruments.Add(new GbPulseInstrument { Id = 2 }));
            AssertInvalid(song => song.Tracks[2].Notes.Add(new Note()));
            AssertInvalid(song => song.SnesEcho.DelayMilliseconds = 17);
            AssertInvalid(song => song.SnesEcho.Feedback = 1);
            AssertInvalid(song => song.SnesEcho.Volume = double.PositiveInfinity);
        }

        /// <summary>不正な効果の種類・範囲・重複を拒否する。</summary>
        [Fact]
        public void RejectsInvalidEffects()
        {
            AssertInvalidEffect(new NoteEffect(NoteEffectKind.None, 0));
            AssertInvalidEffect(new NoteEffect((NoteEffectKind)99, 0));
            AssertInvalidEffect(new NoteEffect(NoteEffectKind.Delay, -1));
            AssertInvalidEffect(new NoteEffect(NoteEffectKind.Delay, 48));
            AssertInvalidEffect(new NoteEffect(NoteEffectKind.VolumeSlide, 16));
            AssertInvalidEffect(new NoteEffect(NoteEffectKind.Arpeggio, 256));
            AssertInvalidEffect(new NoteEffect(NoteEffectKind.Vibrato, -1));
            AssertInvalid(song => song.Tracks[0].Notes.Add(new Note
            {
                Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 1), new NoteEffect(NoteEffectKind.Delay, 2) }
            }));
        }

        /// <summary>明示的な null と不正なマクロを統一例外で拒否する。</summary>
        [Fact]
        public void RejectsNullAndInvalidMacros()
        {
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(null!));
            AssertInvalid(song => song.Tracks = null!);
            AssertInvalid(song => song.Tracks[0] = null!);
            AssertInvalid(song => song.Tracks[0].Notes = null!);
            AssertInvalid(song => song.Tracks[0].Notes.Add(null!));
            AssertInvalid(song => song.Instruments = null!);
            AssertInvalid(song => song.Instruments[0] = null!);
            AssertInvalid(song => song.SnesEcho = null!);
            AssertInvalid(song => ((NesPulseInstrument)song.Instruments[0]).VolumeMacro = new Macro { Values = null! });
            AssertInvalid(song => ((NesPulseInstrument)song.Instruments[0]).VolumeMacro = new Macro { Values = new[] { 16 } });
            AssertInvalid(song => ((NesPulseInstrument)song.Instruments[0]).VolumeMacro = new Macro { Values = new[] { 15 }, LoopIndex = 1 });
            AssertInvalid(song => ((NesPulseInstrument)song.Instruments[0]).Duty = DutyCycle.None);
        }

        /// <summary>空マクロは未指定として許容し、実在しないループ位置は拒否する。</summary>
        [Fact]
        public void AllowsEmptyMacroOnlyWithoutLoop()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.VolumeMacro = new Macro();
            SongValidator.Validate(song);
            instrument.VolumeMacro.LoopIndex = 0;
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        /// <summary>チップ固有パラメータの制約を検証する。</summary>
        [Fact]
        public void RejectsInvalidChipParameters()
        {
            Song gameBoy = SongFactory.Create(ChipKind.GameBoy);
            gameBoy.Instruments[0] = new GbWaveInstrument { Waveform = new int[31] };
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(gameBoy));
            gameBoy.Instruments[0] = new GbNoiseInstrument { LfsrWidth = 8 };
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(gameBoy));
            gameBoy.Instruments[0] = new GbPulseInstrument { InitialVolume = 16 };
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(gameBoy));
            Song snes = SongFactory.Create(ChipKind.Snes);
            snes.Instruments[0] = new SnesSampleInstrument { Envelope = new AdsrEnvelope(-1, 0, 1, 0) };
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(snes));
        }

        private static void AssertInvalid(Action<Song> mutate)
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            mutate(song);
            Assert.Throws<SongValidationException>(() => SongValidator.Validate(song));
        }

        private static void AssertInvalidEffect(NoteEffect effect)
        {
            AssertInvalid(song => song.Tracks[0].Notes.Add(new Note { Effects = new[] { effect } }));
        }
    }
}
