using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Formats.Export
{
    /// <summary>絶対時刻順の不変な発音制御列。合成器や PCM を生成しない。</summary>
    public sealed class ControlTimeline
    {
        internal ControlTimeline(Song snapshot, long endSamples, List<ControlEvent> events)
        {
            Chip = snapshot.Chip;
            Title = snapshot.Title;
            EndSamples = endSamples;
            Events = events.AsReadOnly();
            var tracks = new List<ControlTrack>(snapshot.Tracks.Count);
            foreach (Track track in snapshot.Tracks)
            {
                tracks.Add(new ControlTrack(track));
            }
            Tracks = tracks.AsReadOnly();
            var instruments = new Dictionary<int, ControlInstrument>();
            foreach (Instrument instrument in snapshot.Instruments)
            {
                instruments.Add(instrument.Id, new ControlInstrument(instrument));
            }
            Instruments = new ReadOnlyDictionary<int, ControlInstrument>(instruments);
        }

        /// <summary>対象チップ。</summary>
        public ChipKind Chip { get; }
        /// <summary>作成開始時の曲名。</summary>
        public string Title { get; }
        /// <summary>有限演奏の終端サンプル位置。末尾余白は含めない。</summary>
        public long EndSamples { get; }
        /// <summary>時刻昇順。同時刻は全 Off、On / 更新の順で、それぞれトラック番号昇順。</summary>
        public IReadOnlyList<ControlEvent> Events { get; }
        /// <summary>ソングの固定チャンネル構成を維持したトラック設定。</summary>
        public IReadOnlyList<ControlTrack> Tracks { get; }
        /// <summary>音色識別子から参照する不変な音色設定。</summary>
        public IReadOnlyDictionary<int, ControlInstrument> Instruments { get; }

        /// <summary>SongValidator の検証後に独立コピーを作り、境界だけを進めて制御列を確定する。入力不正は既存例外で返す。</summary>
        public static ControlTimelineResult Create(Song song, ChipExportOptions options)
        {
            SongValidator.Validate(song);
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var report = new ConversionReport(options.Format, song.Chip, options.Strict);
            report.AddLimitation("有限回数の展開済み演奏のみを保存し、無限ループと元の LoopStartTick の復元には対応しません。");
            ConversionLimits.ValidateExportInput(song, options, report);
            if (!report.CanWrite)
            {
                return new ControlTimelineResult(null, report);
            }
            // SNES は上の適合検証で拒否するため、複製でプリセット PCM や BRR の生成へ入らない。
            Song snapshot = SongSerializer.Deserialize(SongSerializer.Serialize(song));
            ConversionLimits.ValidateExportInput(snapshot, options, report);
            if (!report.CanWrite)
            {
                return new ControlTimelineResult(null, report);
            }
            var builder = new ControlTimelineBuilder(snapshot, options, report);
            return new ControlTimelineResult(builder.Build(), report);
        }
    }
}
