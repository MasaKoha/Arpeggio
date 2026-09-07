using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Sfx
{
    /// <summary>既存の音色とノート効果だけで、編集可能な効果音ソングを作る。</summary>
    public static class SfxPresetFactory
    {
        private const int PresetTempo = 150;
        private const int ToneInstrumentId = 1;
        private const int NoiseInstrumentId = 2;
        private const int ToneTrackIndex = 0;
        private const int NoiseTrackIndex = 3;
        private const int SnesNoiseTrackIndex = 1;
        private const int NoteVolume = 12;
        private const int GentleDecay = -6;
        private const int FullDecay = -NoteVolume;

        /// <summary>三チップ共通のプリセットを、そのチップの有効なチャンネルで生成する。</summary>
        public static Song Create(ChipKind chip, SfxPresetKind kind)
        {
            SfxPresetDescription preset = SfxPresetCatalog.Get(kind);
            Song song = SongFactory.Create(chip, PresetTempo, preset.LengthTicks);
            song.Title = preset.Name;
            song.Instruments.Clear();
            song.Instruments.Add(CreateTone(chip));
            switch (kind)
            {
                case SfxPresetKind.Jump: AddJump(song); break;
                case SfxPresetKind.Coin: AddCoin(song); break;
                case SfxPresetKind.Hit: AddHit(song); break;
                case SfxPresetKind.Explosion: AddExplosion(song); break;
                case SfxPresetKind.PowerUp: AddPowerUp(song); break;
                case SfxPresetKind.Laser: AddLaser(song); break;
                case SfxPresetKind.Blip: AddBlip(song); break;
                case SfxPresetKind.Select: AddSelect(song); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            SongValidator.Validate(song);
            return song;
        }

        private static Instrument CreateTone(ChipKind chip)
        {
            switch (chip)
            {
                case ChipKind.Nes:
                    return new NesPulseInstrument { Id = ToneInstrumentId, Name = "sfx-pulse", Duty = DutyCycle.Percent25 };
                case ChipKind.GameBoy:
                    return new GbPulseInstrument { Id = ToneInstrumentId, Name = "sfx-pulse", Duty = DutyCycle.Percent25 };
                case ChipKind.Snes:
                    return new SnesSampleInstrument
                    {
                        Id = ToneInstrumentId, Name = "sfx-pulse", Waveform = SnesWaveformKind.Pulse,
                        Loop = true, Envelope = new AdsrEnvelope(0, 0, 1, 0)
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(chip));
            }
        }

        private static void AddJump(Song song)
        {
            const int StartPitch = 55;
            const int RiseSemitones = 19;
            AddTone(song, 0, song.LengthTicks, StartPitch, new NoteEffect(NoteEffectKind.PitchSlide, RiseSemitones));
        }

        private static void AddCoin(Song song)
        {
            const int FirstPitch = 67;
            const int SecondPitch = 74;
            const int FirstDuration = 6;
            const int OctaveFigure = 0x07;
            AddTone(song, 0, FirstDuration, FirstPitch);
            AddTone(song, FirstDuration, song.LengthTicks - FirstDuration, SecondPitch, new NoteEffect(NoteEffectKind.Arpeggio, OctaveFigure));
        }

        private static void AddHit(Song song)
        {
            const int StartPitch = 55;
            const int FallSemitones = -12;
            const int NoiseDuration = 8;
            AddTone(song, 0, song.LengthTicks, StartPitch, new NoteEffect(NoteEffectKind.PitchSlide, FallSemitones));
            AddNoise(song, NoiseDuration, NoiseMode.Short);
        }

        private static void AddExplosion(Song song)
        {
            AddNoise(song, song.LengthTicks, NoiseMode.Long);
        }

        private static void AddPowerUp(Song song)
        {
            const int StageCount = 4;
            const int MajorFigure = 0x47;
            int[] pitches = { 55, 60, 64, 67 };
            int duration = song.LengthTicks / StageCount;
            for (int stage = 0; stage < StageCount; stage++)
            {
                AddTone(song, stage * duration, duration, pitches[stage], new NoteEffect(NoteEffectKind.Arpeggio, MajorFigure));
            }
        }

        private static void AddLaser(Song song)
        {
            const int StartPitch = 81;
            const int FallSemitones = -36;
            AddTone(song, 0, song.LengthTicks, StartPitch, new NoteEffect(NoteEffectKind.PitchSlide, FallSemitones));
        }

        private static void AddBlip(Song song)
        {
            const int Pitch = 72;
            AddTone(song, 0, song.LengthTicks, Pitch);
        }

        private static void AddSelect(Song song)
        {
            const int FirstPitch = 67;
            const int SecondPitch = 72;
            int duration = song.LengthTicks / 2;
            AddTone(song, 0, duration, FirstPitch);
            AddTone(song, duration, duration, SecondPitch);
        }

        private static void AddTone(Song song, int tick, int duration, int pitch, NoteEffect effect = default)
        {
            NoteEffect decay = new NoteEffect(NoteEffectKind.VolumeSlide, GentleDecay);
            song.Tracks[ToneTrackIndex].Notes.Add(new Note
            {
                Tick = tick, DurationTicks = duration, MidiNote = pitch,
                Volume = NoteVolume, InstrumentId = ToneInstrumentId,
                Effects = effect.Kind == NoteEffectKind.None ? new[] { decay } : new[] { effect, decay }
            });
        }

        private static void AddNoise(Song song, int duration, NoiseMode mode)
        {
            const int NesShortPeriod = 3;
            const int NesLongPeriod = 12;
            const int GameBoyShortPeriod = 120;
            const int GameBoyLongPeriod = 96;
            const int SnesShortPitch = 60;
            const int SnesLongPitch = 48;
            int track = song.Chip == ChipKind.Snes ? SnesNoiseTrackIndex : NoiseTrackIndex;
            int pitch;
            switch (song.Chip)
            {
                case ChipKind.Nes:
                    song.Instruments.Add(new NesNoiseInstrument { Id = NoiseInstrumentId, Name = "sfx-noise", NoiseMode = mode });
                    pitch = mode == NoiseMode.Short ? NesShortPeriod : NesLongPeriod;
                    break;
                case ChipKind.GameBoy:
                    const int ShortWidth = 7;
                    const int LongWidth = 15;
                    song.Instruments.Add(new GbNoiseInstrument { Id = NoiseInstrumentId, Name = "sfx-noise", LfsrWidth = mode == NoiseMode.Short ? ShortWidth : LongWidth });
                    pitch = mode == NoiseMode.Short ? GameBoyShortPeriod : GameBoyLongPeriod;
                    break;
                case ChipKind.Snes:
                    song.Instruments.Add(new SnesSampleInstrument
                    {
                        Id = NoiseInstrumentId, Name = "sfx-noise", Waveform = SnesWaveformKind.Noise,
                        Loop = true, Envelope = new AdsrEnvelope(0, 0, 1, 0)
                    });
                    pitch = mode == NoiseMode.Short ? SnesShortPitch : SnesLongPitch;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(song));
            }
            song.Tracks[track].Notes.Add(new Note
            {
                DurationTicks = duration, MidiNote = pitch, Volume = NoteVolume, InstrumentId = NoiseInstrumentId,
                Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, FullDecay) }
            });
        }
    }
}
