using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nes;

namespace Arpeggio.Core.Tests.Formats.Export
{
    /// <summary>レジスタ列の所有権と複数変換の状態分離を検証する。</summary>
    public sealed class RegisterTimelineTests
    {
        /// <summary>元 Song・レポート・後続変換を変更しても、確定した列は変化しない。</summary>
        [Fact]
        public void CompiledWritesAreReadOnlyAndIndependentOfLaterConversions()
        {
            const int SongTicks = 4;
            const int ConcertNote = 69;
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: SongTicks);
            song.Tracks[0].Notes.Add(new Note { DurationTicks = SongTicks, MidiNote = ConcertNote });
            var options = new ChipExportOptions { Format = ConversionFormat.Vgm };
            ControlTimelineResult control = ControlTimeline.Create(song, options);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            RegisterWrite[] captured = timeline.Writes.ToArray();
            var list = Assert.IsAssignableFrom<IList<RegisterWrite>>(timeline.Writes);
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list.Clear());
            Assert.Throws<NotSupportedException>(() => list[0] = default);
            song.Tracks[0].Notes[0].MidiNote = 0;
            song.LengthTicks *= 2;
            control.Report.AddWarning(new ConversionDiagnostic("LaterWarning", "後続診断による所有権確認。"));
            ControlTimelineResult changedControl = ControlTimeline.Create(song, options);
            Assert.NotNull(changedControl.Timeline);
            Assert.NotNull(NesRegisterCompiler.Compile(changedControl.Timeline, changedControl.Report));
            var freshReport = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict: true);
            RegisterTimeline? repeated = NesRegisterCompiler.Compile(control.Timeline, freshReport);
            Assert.NotNull(repeated);
            Assert.Equal(captured, repeated.Writes.ToArray());
            Assert.Equal(captured, timeline.Writes.ToArray());
            Assert.Equal(control.Timeline.EndSamples, timeline.EndSamples);
            Assert.Equal((long)captured.Length, freshReport.Statistics["registerWrites"]);
            Assert.True(freshReport.CanWrite);
        }

        /// <summary>既存のエラーがあるレポートから成功列を作らない。</summary>
        [Fact]
        public void ExistingErrorPreventsPartialTimeline()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(control.Timeline);
            control.Report.AddError(new ConversionDiagnostic("ExistingError", "先行変換の失敗。"));
            Assert.Null(NesRegisterCompiler.Compile(control.Timeline, control.Report));
            Assert.Single(control.Report.Errors);
        }
    }
}
