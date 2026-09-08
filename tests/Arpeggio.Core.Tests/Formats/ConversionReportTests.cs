using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>診断の集約・保持上限と、全発生数を使う strict 判定を検証する。</summary>
    public sealed class ConversionReportTests
    {
        /// <summary>恒常的な制限だけでは strict を失敗させず、変換警告だけを保存拒否に使う。</summary>
        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void StrictUsesWarningsAndExcludesLimitations(bool strict, bool expectedCanWrite)
        {
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict);
            report.AddLimitation("有限展開のみ");
            report.AddLimitation("有限展開のみ");
            Assert.True(report.CanWrite);
            Assert.Single(report.Limitations);
            report.AddWarning(new ConversionDiagnostic("VolumeQuantized", "音量を丸めました。"));
            Assert.Equal(expectedCanWrite, report.CanWrite);
            Assert.Empty(report.Errors);
            Assert.Equal(1, report.WarningCount);
        }

        /// <summary>警告・エラーを各 4096 明細に制限し、超過分の全数とコード別数を維持する。</summary>
        [Fact]
        public void DetailLimitsPreserveTotalAndCodeCounts()
        {
            const int DetailLimit = 4096;
            const int ExcessCount = 7;
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict: true);
            for (int sourceEvent = 0; sourceEvent < DetailLimit + ExcessCount; sourceEvent++)
            {
                var diagnostic = new ConversionDiagnostic("PitchClamped", "音程を制限しました。") { SourceEvent = sourceEvent };
                report.AddWarning(diagnostic);
                report.AddError(diagnostic with { Code = "ControlEventCollision" });
            }
            Assert.Equal(DetailLimit, report.Warnings.Count);
            Assert.Equal(DetailLimit, report.Errors.Count);
            Assert.Equal(DetailLimit + ExcessCount, report.WarningCount);
            Assert.Equal(DetailLimit + ExcessCount, report.ErrorCount);
            Assert.Equal(ExcessCount, report.DroppedWarningCount);
            Assert.Equal(ExcessCount, report.DroppedErrorCount);
            Assert.Equal(DetailLimit + ExcessCount, report.WarningCountsByCode["PitchClamped"]);
            Assert.Equal(DetailLimit + ExcessCount, report.ErrorCountsByCode["ControlEventCollision"]);
            Assert.False(report.CanWrite);
        }

        /// <summary>警告の明細を一件も表示しない場合も strict は変換損失を拒否する。</summary>
        [Fact]
        public void StrictRejectsWarningsEvenWhenNoDetailIsRetained()
        {
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            report.AddWarning(new ConversionDiagnostic("PanReduced", "パンを省略しました。") { OccurrenceCount = 3 });
            Assert.Empty(report.Warnings);
            Assert.Equal(3, report.WarningCount);
            Assert.Equal(3, report.DroppedWarningCount);
            Assert.Equal(3, report.WarningCountsByCode["PanReduced"]);
            Assert.False(report.CanWrite);
        }

        /// <summary>同一元ノートの更新を集約し、最大誤差の値と発生数を保持する。</summary>
        [Fact]
        public void MacroUpdatesAggregateBySourceAndKeepMaximumError()
        {
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes, diagnosticDetailLimit: 1);
            var diagnostic = new ConversionDiagnostic("PitchClamped", "変調音程を制限しました。")
            {
                SourceTrack = 0, SourceEvent = 2, SourceTick = 12, OutputTrack = 0,
                OutputTick = 12, Original = "30", Converted = "33", MaximumError = 3
            };
            report.AddWarning(diagnostic);
            report.AddWarning(diagnostic with { OutputTick = 13, Original = "29", MaximumError = 4, OccurrenceCount = 2 });
            report.AddWarning(diagnostic with { OutputTick = 14, MaximumError = 1 });
            ConversionDiagnostic aggregated = Assert.Single(report.Warnings);
            Assert.Equal(4, aggregated.OccurrenceCount);
            Assert.Equal(4.0, aggregated.MaximumError);
            Assert.Equal("29", aggregated.Original);
            Assert.Equal(13, aggregated.OutputTick);
            Assert.Equal(0, report.DroppedWarningCount);
            Assert.Equal(4, report.WarningCountsByCode["PitchClamped"]);
        }

        /// <summary>別の元ノートを集約せず、明細と辞書の外部変更を拒否する。</summary>
        [Fact]
        public void DistinctSourcesAndReadOnlyViewsArePreserved()
        {
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.GameBoy);
            var diagnostic = new ConversionDiagnostic("ProgramApproximated", "音色を置換しました。") { SourceChannel = 1 };
            report.AddWarning(diagnostic);
            report.AddWarning(diagnostic with { SourceChannel = 2 });
            Assert.Equal(2, report.Warnings.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<ConversionDiagnostic>)report.Warnings).Clear());
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)report.WarningCountsByCode).Clear());
        }

        /// <summary>エラーは strict の有無にかかわらず保存を拒否する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ErrorsAlwaysRejectWriting(bool strict)
        {
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict, diagnosticDetailLimit: 0);
            report.AddError(new ConversionDiagnostic("OutputSizeLimitExceeded", "容量超過です。"));
            Assert.Empty(report.Errors);
            Assert.False(report.CanWrite);
        }

        /// <summary>診断の不正な発生数や非有限の誤差を拒否する。</summary>
        [Fact]
        public void InvalidDiagnosticsDoNotChangeCounters()
        {
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes);
            var diagnostic = new ConversionDiagnostic("PitchClamped", "音程を制限しました。");
            Assert.Throws<ArgumentException>(() => report.AddWarning(diagnostic with { OccurrenceCount = 0 }));
            Assert.Throws<ArgumentException>(() => report.AddWarning(diagnostic with { MaximumError = double.NaN }));
            Assert.Throws<ArgumentException>(() => report.AddWarning(diagnostic with { SourceChannel = 0 }));
            Assert.Equal(0, report.WarningCount);
        }
    }
}
