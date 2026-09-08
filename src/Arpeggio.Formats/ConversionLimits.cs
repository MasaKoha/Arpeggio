using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Sequencing;
using Arpeggio.Formats.Export;

namespace Arpeggio.Formats
{
    /// <summary>形式変換の固定資源上限と、書き込み前に行う共通検証。</summary>
    public static class ConversionLimits
    {
        private const double SecondsPerMinute = 60.0;
        /// <summary>制御時刻の固定サンプルレート。</summary>
        public const int ControlSampleRate = 44100;
        /// <summary>初回を含む最大再生回数。</summary>
        public const int MaximumLoops = 16;
        /// <summary>展開後の最大演奏秒数。</summary>
        public const int MaximumDurationSeconds = 1800;
        /// <summary>展開前の最大ノート数。</summary>
        public const int MaximumSourceNotes = 250000;
        /// <summary>最大レジスタ書き込み数。</summary>
        public const int MaximumRegisterWrites = 4000000;
        /// <summary>VGM の最大ファイルサイズ。</summary>
        public const int MaximumVgmBytes = 64 * 1024 * 1024;
        /// <summary>NSF の最大 ROM サイズ。ヘッダーを含まない。</summary>
        public const int MaximumNsfRomBytes = 1024 * 1024;
        /// <summary>NSF のプレイヤー用固定 bank サイズ。</summary>
        public const int NsfPlayerBytes = 4096;
        /// <summary>NSF の最大曲データサイズ。</summary>
        public const int MaximumNsfDataBytes = MaximumNsfRomBytes - NsfPlayerBytes;
        /// <summary>NSF v1 のヘッダーサイズ。</summary>
        public const int NsfHeaderBytes = 128;
        /// <summary>タイトル・author・copyright ごとの UTF-16 コード単位上限。</summary>
        public const int MaximumMetadataLength = 1024;
        /// <summary>警告・エラーそれぞれの明細保持上限。</summary>
        public const int MaximumDiagnosticDetails = 4096;
        /// <summary>MIDI 入力バイト上限。</summary>
        public const int MaximumMidiBytes = 32 * 1024 * 1024;
        /// <summary>MIDI のイベント数上限。</summary>
        public const int MaximumMidiEvents = 1000000;
        /// <summary>MIDI の NoteOn 数上限。</summary>
        public const int MaximumMidiNoteOns = 250000;
        /// <summary>MIDI のトラック数上限。</summary>
        public const int MaximumMidiTracks = 256;
        /// <summary>MIDI のユーザー表記チャンネル番号の上限。</summary>
        public const int MaximumMidiChannel = 16;

        /// <summary>検証済み Song の形式適合・設定・展開前資源を検査する。ファイル操作は行わない。</summary>
        public static void ValidateExportInput(Song song, ChipExportOptions options, ConversionReport report)
        {
            bool supportedFormat = options.Format == ConversionFormat.Nsf || options.Format == ConversionFormat.Vgm;
            if (!supportedFormat)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedFormat", "書き出し形式は NSF または VGM を指定してください。"));
            }
            bool supportedChip = song.Chip == ChipKind.Nes || (song.Chip == ChipKind.GameBoy && options.Format == ConversionFormat.Vgm);
            if (!supportedChip)
            {
                report.AddError(new ConversionDiagnostic("UnsupportedChip", "この形式は指定チップに対応していません。"));
            }
            ValidateMetadata(song.Title, "title", report);
            ValidateMetadata(options.Author, "author", report);
            ValidateMetadata(options.Copyright, "copyright", report);
            if (options.Format == ConversionFormat.Vgm && options.Copyright != string.Empty)
            {
                report.AddError(new ConversionDiagnostic("InvalidOptions", "copyright は NSF 専用です。"));
            }
            if (options.Loops < 1 || options.Loops > MaximumLoops)
            {
                report.AddError(new ConversionDiagnostic("InvalidOptions", "loops は 1〜16 を指定してください。"));
                return;
            }
            long noteCount = 0;
            foreach (Track track in song.Tracks)
            {
                noteCount = checked(noteCount + track.Notes.Count);
            }
            report.SetStatistic("sourceNotes", noteCount);
            CheckMaximum(noteCount, MaximumSourceNotes, "SourceNoteLimitExceeded", "元ノート数の上限を超えています。", report);
            var clock = new TickClock(song.TempoBpm, ControlSampleRate);
            double totalTicks = song.LengthTicks + (options.Loops - 1.0) * (song.LengthTicks - song.LoopStartTick);
            double durationSeconds = totalTicks * SecondsPerMinute / song.TempoBpm / Song.FixedTicksPerBeat;
            report.SetOutputMetrics(durationSeconds, 0);
            // サンプルへ丸めてから制限すると、上限を僅かに超える入力を受理してしまう。
            if (durationSeconds > MaximumDurationSeconds)
            {
                report.AddError(new ConversionDiagnostic("DurationLimitExceeded", "展開後の演奏時間が 1800 秒を超えています。"));
                return;
            }
            report.SetOutputMetrics(clock.TickToSamples(totalTicks) / (double)ControlSampleRate, 0);
        }

        /// <summary>全候補のレジスタ数と予定サイズを保存前に検証する。NSF 曲データは bank パディング前の値を渡す。</summary>
        public static void ValidateExportSize(ConversionReport report, long registerWrites, long outputBytes, long nsfDataBytes = 0)
        {
            if (registerWrites < 0 || outputBytes < 0 || nsfDataBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputBytes));
            }
            report.SetOutputMetrics(report.DurationSeconds, outputBytes);
            report.SetStatistic("registerWrites", registerWrites);
            CheckMaximum(registerWrites, MaximumRegisterWrites, "RegisterWriteLimitExceeded", "レジスタ書き込み数の上限を超えています。", report);
            if (report.Format == ConversionFormat.Vgm)
            {
                CheckMaximum(outputBytes, MaximumVgmBytes, "OutputSizeLimitExceeded", "VGM の 64 MiB 上限を超えています。", report);
            }
            else if (report.Format == ConversionFormat.Nsf)
            {
                CheckMaximum(outputBytes, MaximumNsfRomBytes + NsfHeaderBytes, "OutputSizeLimitExceeded", "NSF の ROM 上限を超えています。", report);
                CheckMaximum(nsfDataBytes, MaximumNsfDataBytes, "NsfDataLimitExceeded", "NSF の曲データ上限を超えています。", report);
            }
            else
            {
                report.AddError(new ConversionDiagnostic("UnsupportedFormat", "サイズ検証の対象は NSF または VGM です。"));
            }
        }

        /// <summary>MIDI reader が確保・読み取りを進める前に入力資源を検証する。</summary>
        public static void ValidateMidiSize(ConversionReport report, long inputBytes, long eventCount, long noteOnCount, double durationSeconds)
        {
            if (inputBytes < 0 || eventCount < 0 || noteOnCount < 0 || !double.IsFinite(durationSeconds) || durationSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inputBytes));
            }
            CheckMaximum(inputBytes, MaximumMidiBytes, "MidiInputLimitExceeded", "MIDI 入力の 32 MiB 上限を超えています。", report);
            CheckMaximum(eventCount, MaximumMidiEvents, "MidiEventLimitExceeded", "MIDI イベント数の上限を超えています。", report);
            CheckMaximum(noteOnCount, MaximumMidiNoteOns, "SourceNoteLimitExceeded", "MIDI NoteOn 数の上限を超えています。", report);
            if (durationSeconds > MaximumDurationSeconds)
            {
                report.AddError(new ConversionDiagnostic("DurationLimitExceeded", "MIDI の演奏時間が 1800 秒を超えています。"));
            }
        }

        internal static void ValidateMetadata(string value, string name, ConversionReport report)
        {
            if (value is null || value.Length > MaximumMetadataLength || value.IndexOf('\0') >= 0)
            {
                report.AddError(new ConversionDiagnostic("InvalidMetadata", $"{name} は NUL を含まない 1024 UTF-16 コード単位以下の文字列です。") { Original = name });
            }
        }

        private static void CheckMaximum(long value, long maximum, string code, string message, ConversionReport report)
        {
            if (value > maximum)
            {
                report.AddError(new ConversionDiagnostic(code, message));
            }
        }
    }
}
