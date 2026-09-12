using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Core.Tests.Formats.Midi.Import.Voice
{
    /// <summary>同音 FIFO・MTrk 横断・元位置・終端補完を固定ノート区間で検証する。</summary>
    public sealed class MidiNoteCollectorTests
    {
        /// <summary>同じ ch / pitch の Off は元トラックによらず先に始まった On へ対応する。</summary>
        [Fact]
        public void SamePitchUsesFifoAcrossTracks()
        {
            byte[] first = MidiFileFixture.Bytes("00 90 3C 7F 02 90 3C 40 08 80 3C 00 0A FF 2F 00");
            byte[] second = MidiFileFixture.Bytes("05 80 3C 00 0F FF 2F 00");
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = Collect(report, first, second);
            Assert.Equal(2, notes.Count);
            AssertNote(notes[0], 0, 5, 60, 12);
            AssertNote(notes[1], 2, 10, 60, 6);
            Assert.Equal(0, notes[0].Source.SourceTrack);
            Assert.Equal(0L, notes[0].Source.SourceEvent);
            Assert.Equal(1L, notes[1].Source.SourceEvent);
            Assert.Empty(report.Warnings);
        }

        /// <summary>異なるチャンネルの同音は互いの待ち行列に影響しない。</summary>
        [Fact]
        public void ChannelsHaveIndependentFifoQueues()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = Collect(report, MidiFileFixture.Bytes("00 90 3C 7F 01 91 3C 7F 01 81 3C 00 03 80 3C 00 00 FF 2F 00"));
            AssertNote(notes[0], 0, 5, 60, 12);
            AssertNote(notes[1], 1, 2, 60, 12);
            Assert.Equal(1, notes[0].Source.Channel);
            Assert.Equal(2, notes[1].Source.Channel);
            Assert.Empty(report.Warnings);
        }

        /// <summary>velocity 0 の On は FIFO の Off となる。</summary>
        [Fact]
        public void VelocityZeroReleasesOldestNote()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = Collect(report, MidiFileFixture.Bytes("00 90 3C 7F 01 3C 40 01 3C 00 01 3C 00 00 FF 2F 00"));
            Assert.Equal(new long[] { 2, 3 }, notes.Select(note => note.EndTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>各 MTrk の EOT は共有 ch を閉じず、曲全体の最遅 EOT で未終了音を閉じる。</summary>
        [Fact]
        public void UnclosedNotesUseGlobalEndAndPreserveOnPosition()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = Collect(report,
                MidiFileFixture.Bytes("02 93 3C 7F 03 FF 2F 00"),
                MidiFileFixture.Bytes("0A FF 2F 00"));
            MidiNote note = Assert.Single(notes);
            AssertNote(note, 2, 10, 60, 12);
            Assert.Null(note.KeyOffTick);
            ConversionDiagnostic warning = Assert.Single(report.Warnings);
            Assert.Equal("UnclosedNote", warning.Code);
            Assert.Equal(0, warning.SourceTrack);
            Assert.Equal(4, warning.SourceChannel);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.Equal(2L, warning.SourceTick);
        }

        /// <summary>不明 Off の元位置を保持し、strict でも後続の未終了音まで診断する。</summary>
        [Fact]
        public void UnknownOffAndStrictCollectAllDiagnostics()
        {
            ConversionReport report = MidiFileFixture.Report(strict: true);
            IReadOnlyList<MidiNote> notes = Collect(report, MidiFileFixture.Bytes("01 82 3C 00 01 92 3D 7F 03 FF 2F 00"));
            Assert.Single(notes);
            Assert.False(report.CanWrite);
            Assert.Equal(2L, report.WarningCount);
            Assert.Contains(report.Warnings, diagnostic => diagnostic.Code == "UnmatchedNoteOff" && diagnostic.SourceEvent == 0 && diagnostic.SourceChannel == 3 && diagnostic.SourceTick == 1);
            Assert.Contains(report.Warnings, diagnostic => diagnostic.Code == "UnclosedNote" && diagnostic.SourceEvent == 1);
        }

        /// <summary>同 tick の On→Off は破棄し、Off→On の順では後続 On が残る。</summary>
        [Theory]
        [InlineData("00 90 3C 7F 00 80 3C 00 01 FF 2F 00", 0, "ZeroLengthNoteDropped")]
        [InlineData("00 80 3C 00 00 90 3C 7F 01 80 3C 00 00 FF 2F 00", 1, "UnmatchedNoteOff")]
        public void SameTickOrderIsNotRewritten(string track, int count, string warningCode)
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = Collect(report, MidiFileFixture.Bytes(track));
            Assert.Equal(count, notes.Count);
            Assert.Equal(warningCode, Assert.Single(report.Warnings).Code);
        }

        /// <summary>On と Off が別 MTrk の同 tick にある場合も元トラック番号で順序を固定する。</summary>
        [Fact]
        public void SameTickCrossTrackOrderIsDeterministic()
        {
            byte[] onset = MidiFileFixture.Bytes("00 90 3C 7F 02 FF 2F 00");
            byte[] release = MidiFileFixture.Bytes("00 80 3C 00 02 FF 2F 00");
            ConversionReport firstReport = MidiFileFixture.Report();
            Assert.Empty(Collect(firstReport, onset, release));
            Assert.Equal("ZeroLengthNoteDropped", Assert.Single(firstReport.Warnings).Code);
            ConversionReport secondReport = MidiFileFixture.Report();
            MidiNote note = Assert.Single(Collect(secondReport, release, onset));
            Assert.Equal(2L, note.EndTick);
            Assert.Equal(1, note.Source.SourceTrack);
            Assert.Equal(2L, secondReport.WarningCount);
        }

        /// <summary>収集の再実行で状態を持ち越さず、外部から完成列を変更できない。</summary>
        [Fact]
        public void ResultsAreImmutableAndEachCollectionHasFreshState()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes("00 C0 05 00 90 3C 7F 01 80 3C 00 00 FF 2F 00")), report);
            IReadOnlyList<MidiNote>? first = MidiNoteCollector.Collect(file, report);
            Assert.NotNull(first);
            IReadOnlyList<MidiNote>? second = MidiNoteCollector.Collect(file, MidiFileFixture.Report());
            Assert.NotNull(second);
            Assert.Equal(first.Select(note => (note.Source, note.EndTick, note.Program)), second.Select(note => (note.Source, note.EndTick, note.Program)));
            Assert.Throws<NotSupportedException>(() => ((IList<MidiNote>)first).Clear());
            IReadOnlyList<MidiNote> fresh = Collect(MidiFileFixture.Report(), MidiFileFixture.Bytes("00 90 3C 7F 01 80 3C 00 00 FF 2F 00"));
            Assert.Equal(0, Assert.Single(fresh).Program);
            report.AddError(new ConversionDiagnostic("ExistingError", "先行失敗。"));
            Assert.Null(MidiNoteCollector.Collect(file, report));
            Assert.Single(first);
        }

        internal static IReadOnlyList<MidiNote> Collect(ConversionReport report, params byte[][] tracks)
        {
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(tracks), report);
            IReadOnlyList<MidiNote>? notes = MidiNoteCollector.Collect(file, report);
            Assert.NotNull(notes);
            Assert.Empty(report.Errors);
            return notes;
        }

        private static void AssertNote(MidiNote note, long start, long end, int pitch, int volume)
        {
            Assert.Equal(start, note.StartTick);
            Assert.Equal(end, note.EndTick);
            Assert.Equal(pitch, note.Pitch);
            Assert.Equal(volume, note.Volume);
        }
    }
}
