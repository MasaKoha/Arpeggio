using System;
using System.Collections.Generic;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;

namespace Arpeggio.Core.Document
{
    /// <summary>保存と読み込みの境界でソングの整合性を保証する。</summary>
    public static class SongValidator
    {
        private const int SnesFirTapCount = 8;
        private const int MaximumMidiNote = 127;
        private const int MaximumVolume = 15;
        private const int MaximumArpeggio = 255;
        private const int EchoDelayStep = 16;
        private const int MaximumEchoDelay = 240;

        /// <summary>不正なソングを具体的な理由を持つ例外で拒否する。</summary>
        public static void Validate(Song song)
        {
            if (song is null)
            {
                throw new SongValidationException("ソングが null です。");
            }
            Require(song.Version == Song.CurrentVersion, "version は 1 固定です。");
            Require(song.TicksPerBeat == Song.FixedTicksPerBeat, "ticksPerBeat は 48 固定です。");
            Require(song.TempoBpm > 0, "tempoBpm は正の整数です。");
            Require(song.LengthTicks > 0, "lengthTicks は正の整数です。");
            Require(song.LoopStartTick >= 0 && song.LoopStartTick < song.LengthTicks, "loopStartTick は曲の範囲内です。");
            Require(song.Title != null, "title は null にできません。");
            ValidateEcho(song.SnesEcho);
            Dictionary<int, Instrument> instruments = ValidateInstruments(song);
            ValidateTracks(song, instruments);
            SfxDefinitionValidator.Validate(song.Sfx, song.Chip);
        }

        private static Dictionary<int, Instrument> ValidateInstruments(Song song)
        {
            if (song.Instruments is null)
            {
                throw new SongValidationException("instruments は配列です。");
            }
            var instruments = new Dictionary<int, Instrument>();
            foreach (Instrument instrument in song.Instruments)
            {
                InstrumentValidator.Validate(instrument, song.Chip);
                Require(!instruments.ContainsKey(instrument.Id), $"音色 ID {instrument.Id} が重複しています。");
                instruments.Add(instrument.Id, instrument);
            }
            return instruments;
        }

        private static void ValidateTracks(Song song, Dictionary<int, Instrument> instruments)
        {
            ChannelKind[] channels = ChipLayout.GetChannels(song.Chip);
            if (song.Tracks is null || song.Tracks.Count != channels.Length)
            {
                throw new SongValidationException("tracks の数がチップ構成と一致しません。");
            }
            var channelCounts = new Dictionary<ChannelKind, int>();
            for (int index = 0; index < channels.Length; index++)
            {
                Track track = song.Tracks[index];
                if (track is null)
                {
                    throw new SongValidationException("track は null にできません。");
                }
                channelCounts.TryGetValue(channels[index], out int channelIndex);
                Require(track.Channel == channels[index] && track.ChannelIndex == channelIndex, $"track {index} のチャンネル構成が不正です。");
                channelCounts[channels[index]] = channelIndex + 1;
                Require(track.Name != null, "track.name は null にできません。");
                Require(IsInRange(track.Pan, -1, 1), "track.pan は -1〜1 です。");
                ValidateDefaultInstrument(track, song.Chip, instruments);
                ValidateNotes(track, song.LengthTicks, instruments);
            }
        }

        private static void ValidateDefaultInstrument(Track track, ChipKind chip, Dictionary<int, Instrument> instruments)
        {
            if (track.DefaultInstrumentId is not int instrumentId)
            {
                return;
            }
            Require(chip == ChipKind.Snes, "defaultInstrumentId は SNES のバンク用です。");
            if (!instruments.TryGetValue(instrumentId, out Instrument? instrument))
            {
                throw new SongValidationException($"既定音色 ID {instrumentId} が存在しません。");
            }
            Require(InstrumentValidator.GetChannel(instrument.Kind) == track.Channel, "既定音色とチャンネルの種類が一致しません。");
        }

        private static void ValidateNotes(Track track, int lengthTicks, Dictionary<int, Instrument> instruments)
        {
            if (track.Notes is null)
            {
                throw new SongValidationException("notes は配列です。");
            }
            long previousEnd = 0;
            foreach (Note note in track.Notes)
            {
                if (note is null)
                {
                    throw new SongValidationException("note は null にできません。");
                }
                Require(note.Tick >= 0 && note.Tick < lengthTicks, "note.tick が範囲外です。");
                Require(note.DurationTicks > 0 && (long)note.Tick + note.DurationTicks <= lengthTicks, "note.durationTicks が範囲外です。");
                Require(note.Tick >= previousEnd, "ノートが重複または tick 降順です。");
                Require(note.MidiNote >= 0 && note.MidiNote <= MaximumMidiNote, "midiNote は 0〜127 です。");
                Require(note.Volume >= 0 && note.Volume <= MaximumVolume, "volume は 0〜15 です。");
                if (!instruments.TryGetValue(note.InstrumentId, out Instrument? instrument))
                {
                    throw new SongValidationException($"音色 ID {note.InstrumentId} が存在しません。");
                }
                Require(InstrumentValidator.GetChannel(instrument.Kind) == track.Channel, "音色とチャンネルの種類が一致しません。");
                ValidateEffects(note);
                previousEnd = (long)note.Tick + note.DurationTicks;
            }
        }

        private static void ValidateEffects(Note note)
        {
            if (note.Effects is null)
            {
                throw new SongValidationException("effects は配列です。");
            }
            var kinds = new HashSet<NoteEffectKind>();
            foreach (NoteEffect effect in note.Effects)
            {
                Require(effect.Kind != NoteEffectKind.None && Enum.IsDefined(typeof(NoteEffectKind), effect.Kind), "effect.kind が不正です。");
                Require(kinds.Add(effect.Kind), "同じ種類のエフェクトが重複しています。");
                switch (effect.Kind)
                {
                    case NoteEffectKind.VolumeSlide:
                        Require(effect.Value >= -MaximumVolume && effect.Value <= MaximumVolume, "VolumeSlide は -15〜15 です。");
                        break;
                    case NoteEffectKind.Arpeggio:
                        Require(effect.Value >= 0 && effect.Value <= MaximumArpeggio, "Arpeggio は 0x00〜0xff です。");
                        break;
                    case NoteEffectKind.Delay:
                        Require(effect.Value >= 0 && effect.Value < note.DurationTicks, "Delay は 0 以上かつノート長未満です。");
                        break;
                    case NoteEffectKind.Vibrato:
                        Require(effect.Value >= 0, "Vibrato の深さは 0 以上です。");
                        break;
                }
            }
        }

        private static void ValidateEcho(SnesEchoSettings echo)
        {
            if (echo is null)
            {
                throw new SongValidationException("snesEcho は null にできません。");
            }
            Require(echo.DelayMilliseconds >= 0 && echo.DelayMilliseconds <= MaximumEchoDelay && echo.DelayMilliseconds % EchoDelayStep == 0, "エコー遅延は 0〜240 ms の 16 ms 刻みです。");
            Require(IsInRange(echo.Feedback, -1, 1) && Math.Abs(echo.Feedback) < 1, "エコーフィードバックの絶対値は 1 未満です。");
            Require(IsInRange(echo.Volume, 0, 1), "エコー音量は 0〜1 です。");
            if (echo.FirCoefficients is null || echo.FirCoefficients.Length != SnesFirTapCount)
            {
                throw new SongValidationException("FIR 係数は 8 要素です。");
            }
            foreach (int coefficient in echo.FirCoefficients)
            {
                Require(coefficient >= sbyte.MinValue && coefficient <= sbyte.MaxValue, "FIR 係数は -128〜127 です。");
            }
        }

        internal static bool IsInRange(double value, double minimum, double maximum)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new SongValidationException(message);
            }
        }
    }
}
