using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats.Midi.Import.Tempo;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>SMF 解析・音色変換・声割り当てを統合し、保存前に有効な新規 Song と全診断を確定する。</summary>
    public static class MidiImporter
    {
        private const int SecondsPerMinute = 60;

        /// <summary>呼び出し元所有の入力を閉じずに変換する。入力 byte・options・ファイルを変更せず、I/O 例外は伝播する。</summary>
        public static MidiImportResult Import(Stream stream, MidiImportOptions options)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(options);
            var report = new ConversionReport(ConversionFormat.Midi, options.Chip, options.Strict);
            AddLimitations(report);
            ValidateOptions(options, report);
            if (report.ErrorCount != 0)
            {
                return Failed(report);
            }
            try
            {
                return Convert(stream, options, report);
            }
            catch (OverflowException)
            {
                report.AddError(new ConversionDiagnostic("MidiTimeOverflow", "変換時刻または出力長が整数範囲を超えています。"));
            }
            catch (SongValidationException exception)
            {
                report.AddError(new ConversionDiagnostic("InvalidSong", exception.Message));
            }
            return Failed(report);
        }

        private static MidiImportResult Convert(Stream stream, MidiImportOptions options, ConversionReport report)
        {
            MidiFile? file = MidiReader.Read(stream, report);
            if (file is null)
            {
                return Failed(report);
            }
            IReadOnlyList<MidiNote>? notes = MidiNoteCollector.Collect(file, report);
            MidiTempoMap? tempoMap = MidiTempoMap.Create(file, options, report);
            if (notes is null || tempoMap is null)
            {
                return Failed(report);
            }
            MidiTickQuantizer? quantizer = MidiTickQuantizer.Create(tempoMap, options, report);
            if (quantizer is null)
            {
                return Failed(report);
            }
            IReadOnlyList<MidiVoiceNote>? mapped = MidiInstrumentMapper.Map(file, notes, tempoMap, quantizer, report);
            if (mapped is null)
            {
                return Failed(report);
            }
            IReadOnlyList<MidiVoiceTrack>? tracks = MidiVoiceAllocator.Allocate(mapped, options, report);
            if (tracks is null)
            {
                return Failed(report);
            }
            MidiInstrumentMap? instruments = MidiInstrumentMapper.CreateInstruments(tracks, report);
            if (instruments is null)
            {
                return Failed(report);
            }
            Song song = CreateSong(file, options, tempoMap, quantizer, tracks, instruments);
            return Complete(song, report);
        }

        private static Song CreateSong(MidiFile file, MidiImportOptions options, MidiTempoMap tempoMap,
            MidiTickQuantizer quantizer, IReadOnlyList<MidiVoiceTrack> tracks, MidiInstrumentMap instruments)
        {
            var song = new Song
            {
                Title = options.Title ?? file.TrackName ?? Path.GetFileNameWithoutExtension(options.SourceName),
                Chip = options.Chip, TempoBpm = tempoMap.OutputTempoBpm,
                LengthTicks = quantizer.GetLengthTicks(tracks.SelectMany(track => track.Notes).Select(note => note.EndTick)),
                LoopStartTick = 0, Instruments = new List<Instrument>(instruments.Instruments)
            };
            song.SnesEcho.DelayMilliseconds = 0;
            foreach (MidiVoiceTrack source in tracks)
            {
                var track = new Track
                {
                    Channel = source.Channel, ChannelIndex = source.ChannelIndex,
                    Name = FormattableString.Invariant($"{source.Channel} {source.ChannelIndex + 1}"),
                    Muted = false, Pan = 0, DefaultInstrumentId = null
                };
                foreach (MidiAllocatedNote note in source.Notes)
                {
                    track.Notes.Add(new Note
                    {
                        Tick = note.StartTick, DurationTicks = note.DurationTicks,
                        MidiNote = note.Pitch, Volume = note.Volume, InstrumentId = instruments.GetInstrumentId(note)
                    });
                }
                song.Tracks.Add(track);
            }
            return song;
        }

        private static MidiImportResult Complete(Song song, ConversionReport report)
        {
            ConversionLimits.ValidateMetadata(song.Title, "title", report);
            double duration = (double)song.LengthTicks * SecondsPerMinute / (song.TempoBpm * Song.FixedTicksPerBeat);
            report.SetOutputMetrics(duration, 0);
            if (duration > ConversionLimits.MaximumDurationSeconds)
            {
                report.AddError(new ConversionDiagnostic("DurationLimitExceeded", "量子化・固定 gate 適用後の曲長が 1800 秒を超えています。"));
            }
            if (report.ErrorCount != 0)
            {
                return Failed(report);
            }
            // strict の保存拒否より先に全候補を検証し、診断と保存予定 byte 数を揃える。
            string json = SongSerializer.Serialize(song);
            report.SetOutputMetrics(duration, Encoding.UTF8.GetByteCount(json));
            report.SetStatistic("outputTracks", song.Tracks.Count);
            return new MidiImportResult(song, json, report);
        }

        private static void ValidateOptions(MidiImportOptions options, ConversionReport report)
        {
            if (options.Chip != ChipKind.Nes && options.Chip != ChipKind.GameBoy && options.Chip != ChipKind.Snes)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "MIDI の出力は NES / GB / SNES に対応しています。"));
            }
            if (options.SourceName is null)
            {
                report.AddError(new ConversionDiagnostic("InvalidOptions", "SourceName は null にできません。"));
            }
            if (options.Title != null)
            {
                ConversionLimits.ValidateMetadata(options.Title, "title", report);
            }
        }

        private static void AddLimitations(ConversionReport report)
        {
            report.AddLimitation("GM 音色はチップ既定音色または既存 SNES プリセットへの近似で、元の音色・表情を完全には再現しません。");
            report.AddLimitation("単一整数 BPM・48 ticks/beat に実時間を焼き込み、ベロシティ・CC7・CC11 は NoteOn 時の 4 bit 音量に変換します。");
            report.AddLimitation("打楽器は固定 gate と分類別音色を使い、ハイハット choke・GM2 排他グループ・元のドラム音程を再現しません。");
            if (report.Chip == ChipKind.Snes)
            {
                report.AddLimitation("SNES プリセットの既存の音程偏差・ワンショット終端を維持し、原曲にないエコーは追加しません。");
            }
        }

        private static MidiImportResult Failed(ConversionReport report) => new MidiImportResult(null, null, report);
    }
}
