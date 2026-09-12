using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Core.Tests.Formats.Midi.Import.Tempo;
using Arpeggio.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import.Tempo;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Core.Tests.Formats.Midi.Import
{
    /// <summary>絶対端点の丸め、短音・無音・曲長と長曲の非累積誤差を直接検証する。</summary>
    public sealed class MidiTickQuantizerTests
    {
        /// <summary>正の短音だけを延ばし、元ゼロ長は復活させない。</summary>
        [Fact]
        public void PositiveShortNoteIsExtendedAfterOriginalZeroLengthIsDropped()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = Read("00 90 3C 7F 00 80 3C 00 01 90 3D 7F 01 80 3D 00 00 FF 2F 00", report);
            MidiTickQuantizer quantizer = Create(file, new MidiImportOptions(), report);
            MidiNote source = Assert.Single(MidiNoteCollector.Collect(file, report)!);
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(source));
            Assert.Equal(61, note.Source.Pitch);
            Assert.Equal(0, note.StartTick);
            Assert.Equal(1, note.EndTick);
            Assert.Equal(48, quantizer.GetLengthTicks(new[] { note.EndTick }));
            Assert.Equal(1L, report.WarningCountsByCode["ZeroLengthNoteDropped"]);
            Assert.Equal(1L, report.WarningCountsByCode["ShortNoteExtended"]);
            ConversionDiagnostic timing = Assert.Single(report.Warnings, warning => warning.Code == "MidiTimingQuantized" && warning.SourceEvent == 2);
            Assert.Equal(2L, timing.OccurrenceCount);
            Assert.Equal(0.2, timing.MaximumError);
        }

        /// <summary>中間値はゼロから遠ざけ、開始・終了の丸めを独立させる。</summary>
        [Fact]
        public void HalfGridRoundsAwayFromZeroAndKeepsIndependentEndpoints()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = Read("05 90 3C 7F 0A 80 3C 00 00 FF 2F 00", report);
            MidiTickQuantizer quantizer = Create(file, new MidiImportOptions(), report);
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(Assert.Single(MidiNoteCollector.Collect(file, report)!)));
            Assert.Equal(1, note.StartTick);
            Assert.Equal(2, note.EndTick);
            Assert.All(report.Warnings, warning => Assert.Equal(0.5, warning.MaximumError));
        }

        /// <summary>入力 EOT・先頭無音・小節未満の終端を保持する。</summary>
        [Fact]
        public void GlobalEndKeepsLeadingAndTrailingSilenceWithoutBarPadding()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(
                MidiFileFixture.Bytes("83 60 90 3C 7F 83 60 80 3C 00 00 FF 2F 00"),
                MidiFileFixture.Bytes("87 6A FF 2F 00")), report);
            MidiTickQuantizer quantizer = Create(file, new MidiImportOptions(), report);
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(Assert.Single(MidiNoteCollector.Collect(file, report)!)));
            Assert.Equal(48, note.StartTick);
            Assert.Equal(96, note.EndTick);
            Assert.Equal(100, quantizer.InputEndTick);
            Assert.Equal(100, quantizer.GetLengthTicks(new[] { note.EndTick }));
            Assert.Equal(109, quantizer.GetLengthTicks(new[] { note.EndTick, 109 }));
        }

        /// <summary>全ての許可グリッドは正の短音をちょうど一単位に伸ばす。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(12)]
        [InlineData(16)]
        [InlineData(24)]
        [InlineData(48)]
        public void EveryDivisorGridExtendsCollapsedPositiveGate(int grid)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = Read("00 90 3C 7F 01 80 3C 00 00 FF 2F 00", report);
            MidiTickQuantizer quantizer = Create(file, new MidiImportOptions { QuantizeTicks = grid }, report);
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(Assert.Single(MidiNoteCollector.Collect(file, report)!)));
            Assert.Equal(grid, note.EndTick);
        }

        /// <summary>不正グリッドでは量子化器を作らない。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(49)]
        public void InvalidGridIsAnOperationError(int grid)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = Read("00 FF 2F 00", report);
            MidiTempoMap map = MidiTempoMapTests.CreateMap(file, new MidiImportOptions(), report);
            Assert.Null(MidiTickQuantizer.Create(map, new MidiImportOptions { QuantizeTicks = grid }, report));
            Assert.Equal("InvalidOptions", Assert.Single(report.Errors).Code);
        }

        /// <summary>多数の端数テンポ区間を通っても、端点の誤差は半グリッド以内に留まる。</summary>
        [Fact]
        public void LongFractionalTempoMapDoesNotAccumulateRoundedDurations()
        {
            const int NoteCount = 10000;
            const int Delta = 37;
            const int Division = 480;
            using var stream = new MemoryStream();
            for (int noteIndex = 0; noteIndex < NoteCount; noteIndex++)
            {
                stream.Write(MidiFileFixture.Bytes(noteIndex % 2 == 0 ? "00 FF 51 03 07 A1 21" : "00 FF 51 03 09 27 C1"));
                stream.Write(MidiFileFixture.Bytes("00 90 3C 7F 25 80 3C 00"));
            }
            stream.Write(MidiFileFixture.Bytes("00 FF 2F 00"));
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(0, Division, stream.ToArray()), report);
            var options = new MidiImportOptions { Tempo = 137 };
            MidiTempoMap map = MidiTempoMapTests.CreateMap(file, options, report);
            MidiTickQuantizer quantizer = MidiTempoMapTests.CreateQuantizer(map, options, report);
            IReadOnlyList<MidiNote> notes = MidiNoteCollector.Collect(file, report)!;
            Assert.Equal(NoteCount, notes.Count);
            const long PairNumerator = Delta * (500001L + 600001L);
            Assert.Equal(NoteCount / 2 * PairNumerator, map.GetTimeNumerator(NoteCount * Delta));
            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                long expectedNumerator = noteIndex / 2 * PairNumerator + (noteIndex % 2 == 0 ? 0 : Delta * 500001L);
                MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(notes[noteIndex]));
                decimal exactStart = (decimal)expectedNumerator * 137 * 48 / (60000000L * Division);
                Assert.InRange(Math.Abs((decimal)note.StartTick - exactStart), 0, 0.5m);
                long endNumerator = expectedNumerator + Delta * (noteIndex % 2 == 0 ? 500001L : 600001L);
                decimal exactEnd = (decimal)endNumerator * 137 * 48 / (60000000L * Division);
                int roundedEnd = (int)Math.Round(exactEnd, MidpointRounding.AwayFromZero);
                Assert.Equal(Math.Max(note.StartTick + 1, roundedEnd), note.EndTick);
            }
            Assert.True(report.DroppedWarningCount > 0);
            Assert.Empty(report.Errors);
        }

        /// <summary>1800 秒の長曲終端でも高 BPM の絶対 tick が一致する。</summary>
        [Fact]
        public void MaximumInputDurationHasExactOutputPosition()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = Read("00 90 3C 7F E9 BC 00 80 3C 00 00 FF 2F 00", report);
            MidiTickQuantizer quantizer = Create(file, new MidiImportOptions { Tempo = 1000 }, report);
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(Assert.Single(MidiNoteCollector.Collect(file, report)!)));
            Assert.Equal(1440000, note.EndTick);
            Assert.Equal(1440000, quantizer.GetLengthTicks(new[] { note.EndTick }));
        }

        /// <summary>固定ドラム gate は元のゼロ gate から独立し、EOT より後にも伸ばせる。</summary>
        [Fact]
        public void FixedRealTimeGateIsAcceptedWithoutUsingOriginalDrumOff()
        {
            ConversionReport report = MidiFileFixture.Report(strict: true);
            MidiFile file = Read("83 60 99 24 7F 00 89 24 00 00 FF 2F 00", report);
            MidiTickQuantizer quantizer = Create(file, new MidiImportOptions(), report);
            MidiNote source = Assert.Single(MidiNoteCollector.Collect(file, report)!);
            Assert.Throws<ArgumentException>(() => quantizer.QuantizeNote(source));
            MidiQuantizedNote note = Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(source, 384000000));
            Assert.Equal(48, note.StartTick);
            Assert.Equal(77, note.EndTick);
            Assert.Equal(77, quantizer.GetLengthTicks(new[] { note.EndTick }));
            Assert.False(report.CanWrite);
            Assert.Null(quantizer.QuantizeNote(source, 240000000));
            Assert.Contains(report.Warnings, warning => warning.Code == "ZeroLengthNoteDropped");
            Assert.Null(quantizer.QuantizeNote(source, long.MaxValue));
            Assert.Equal("DurationLimitExceeded", Assert.Single(report.Errors).Code);
        }

        private static MidiFile Read(string track, ConversionReport report) => MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes(track)), report);

        private static MidiTickQuantizer Create(MidiFile file, MidiImportOptions options, ConversionReport report)
        {
            return MidiTempoMapTests.CreateQuantizer(MidiTempoMapTests.CreateMap(file, options, report), options, report);
        }
    }
}
