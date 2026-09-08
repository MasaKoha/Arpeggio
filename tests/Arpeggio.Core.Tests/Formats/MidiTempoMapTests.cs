using System;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>全トラックの実効テンポと共通整数分子を、手計算した絶対位置で検証する。</summary>
    public sealed class MidiTempoMapTests
    {
        /// <summary>120→60 BPM は元 tick 0 / 480 / 960 を出力 0 / 48 / 144 にする。</summary>
        [Fact]
        public void TempoChangeIntegratesAcrossTheNote()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 FF 51 03 07 A1 20 00 90 3C 7F 83 60 FF 51 03 0F 42 40 83 60 80 3C 00 00 FF 2F 00")), report);
            var options = new MidiImportOptions();
            MidiTempoMap map = CreateMap(file, options, report);
            Assert.Equal(120, map.OutputTempoBpm);
            Assert.Equal(0L, map.GetTimeNumerator(0));
            Assert.Equal(240000000L, map.GetTimeNumerator(480));
            Assert.Equal(720000000L, map.GetTimeNumerator(960));
            Assert.Equal(1200000000L, map.GetTimeNumerator(1440));
            MidiTickQuantizer quantizer = CreateQuantizer(map, options, report);
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(Assert.Single(MidiNoteCollector.Collect(file, report)!)));
            Assert.Equal(0, note.StartTick);
            Assert.Equal(144, note.EndTick);
            Assert.Equal(144, quantizer.InputEndTick);
            Assert.Equal("TempoMapFlattened", Assert.Single(report.Warnings).Code);
        }

        /// <summary>端数 BPM は整数化し、テンポが極端に速い場合は上端に制限する。</summary>
        [Theory]
        [InlineData("07 A1 21", 120)]
        [InlineData("7A 12 00", 8)]
        [InlineData("FF FF FF", 4)]
        [InlineData("00 00 01", 1000)]
        public void FractionalAndOutOfRangeTemposAreRounded(string tempoBytes, int expectedTempo)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes($"00 FF 51 03 {tempoBytes} 01 FF 2F 00")), report);
            MidiTempoMap map = CreateMap(file, new MidiImportOptions(), report);
            Assert.Equal(expectedTempo, map.OutputTempoBpm);
            Assert.Equal("TempoRounded", Assert.Single(report.Warnings).Code);
            Assert.Equal(Convert.ToInt64(tempoBytes.Replace(" ", string.Empty), 16), map.GetTimeNumerator(1));
        }

        /// <summary>テンポ未指定区間は 500000 µs とし、明示 BPM は入力時刻の倍率だけを変える。</summary>
        [Theory]
        [InlineData(null, 120, 48)]
        [InlineData(60, 60, 24)]
        [InlineData(1000, 1000, 400)]
        public void DefaultTempoAndExplicitOutputTempoPreserveRealTime(int? tempo, int expectedTempo, int expectedEnd)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes("83 60 FF 2F 00")), report);
            var options = new MidiImportOptions { Tempo = tempo };
            MidiTempoMap map = CreateMap(file, options, report);
            Assert.Equal(expectedTempo, map.OutputTempoBpm);
            Assert.Equal(240000000L, map.GetTimeNumerator(480));
            Assert.Equal(expectedEnd, CreateQuantizer(map, options, report).InputEndTick);
            Assert.Empty(report.Warnings);
        }

        /// <summary>非 conductor の最終値を採用し、同 tick 内だけで変わって戻るテンポを実効変化に数えない。</summary>
        [Fact]
        public void ConflictsUseLastStableEventWithoutTransientTempoSegments()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(
                MidiFileFixture.Bytes("00 FF 51 03 0B 71 B0 83 60 FF 51 03 0F 42 40 83 60 FF 2F 00"),
                MidiFileFixture.Bytes("00 FF 51 03 07 A1 20 83 60 FF 51 03 07 A1 20 83 60 FF 2F 00")), report);
            MidiTempoMap map = CreateMap(file, new MidiImportOptions(), report);
            Assert.Equal(120, map.OutputTempoBpm);
            Assert.Equal(480000000L, map.GetTimeNumerator(960));
            Assert.Equal(2L, report.WarningCountsByCode["NonConductorTempo"]);
            Assert.Equal(2L, report.WarningCountsByCode["ConflictingTempo"]);
            Assert.DoesNotContain(report.Warnings, warning => warning.Code == "TempoMapFlattened");
            Assert.All(report.Warnings, warning => Assert.Equal(1, warning.SourceTrack));
        }

        /// <summary>tick 0 以降に初めて現れたテンポを基準 BPM へ遡及させない。</summary>
        [Fact]
        public void FirstLateTempoKeepsDefaultReference()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes("83 60 FF 51 03 0F 42 40 83 60 FF 2F 00")), report);
            MidiTempoMap map = CreateMap(file, new MidiImportOptions(), report);
            Assert.Equal(120, map.OutputTempoBpm);
            Assert.Equal(720000000L, map.GetTimeNumerator(960));
        }

        /// <summary>同一トラック・同 tick の最後の値を選び、同値再送は警告しない。</summary>
        [Fact]
        public void SameTrackLastTempoWinsAndRepeatedValueIsNotAConflict()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(0, 480, MidiFileFixture.Bytes(
                "00 FF 51 03 07 A1 20 00 FF 51 03 0F 42 40 01 FF 51 03 0F 42 40 01 FF 2F 00")), report);
            MidiTempoMap map = CreateMap(file, new MidiImportOptions(), report);
            Assert.Equal(60, map.OutputTempoBpm);
            Assert.Equal(2000000L, map.GetTimeNumerator(2));
            ConversionDiagnostic warning = Assert.Single(report.Warnings);
            Assert.Equal("ConflictingTempo", warning.Code);
            Assert.Equal(1L, warning.SourceEvent);
        }

        /// <summary>明示テンポの範囲外は部分結果を返さない。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1001)]
        public void InvalidExplicitTempoReturnsError(int tempo)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00")), report);
            Assert.Null(MidiTempoMap.Create(file, new MidiImportOptions { Tempo = tempo }, report));
            Assert.Equal("InvalidOptions", Assert.Single(report.Errors).Code);
        }

        /// <summary>任意時刻照会は負の tick と整数オーバーフローを隠さない。</summary>
        [Fact]
        public void TimeLookupRejectsNegativeAndOverflowingTicks()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00")), report);
            MidiTempoMap map = CreateMap(file, new MidiImportOptions(), report);
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTimeNumerator(-1));
            Assert.Throws<OverflowException>(() => map.GetTimeNumerator(long.MaxValue));
            report.AddError(new ConversionDiagnostic("PreviousError", "先行エラー。"));
            Assert.Null(MidiTempoMap.Create(file, new MidiImportOptions(), report));
        }

        internal static MidiTempoMap CreateMap(MidiFile file, MidiImportOptions options, ConversionReport report)
        {
            MidiTempoMap? map = MidiTempoMap.Create(file, options, report);
            Assert.NotNull(map);
            return map;
        }

        internal static MidiTickQuantizer CreateQuantizer(MidiTempoMap map, MidiImportOptions options, ConversionReport report)
        {
            MidiTickQuantizer? quantizer = MidiTickQuantizer.Create(map, options, report);
            Assert.NotNull(quantizer);
            return quantizer;
        }
    }
}
