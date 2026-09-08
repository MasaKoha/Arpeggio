using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>古い声の打ち切りと新音破棄を、後続 Off・同時 On・非重複の条件で検証する。</summary>
    public sealed class MidiPolyphonyTests
    {
        /// <summary>steal-oldest は古い音を停止し、drop-new は終端を保持する。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest, 2, 20, "NoteTruncated")]
        [InlineData(MidiPolyphonyMode.DropNew, 1, 100, "PolyphonyReduced")]
        public void ModesChooseBetweenOldGateAndNewNote(MidiPolyphonyMode mode, int count, int firstEnd, string code)
        {
            MidiVoiceNote first = MidiVoiceFixture.Note(60, startTick: 0, endTick: 100);
            MidiVoiceNote second = MidiVoiceFixture.Note(64, startTick: 20, endTick: 150);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first, second }, report, mode, MidiVoiceFixture.Map(1, 0));
            Assert.Equal(count, tracks[0].Notes.Count);
            Assert.Equal(firstEnd, tracks[0].Notes[0].EndTick);
            ConversionDiagnostic warning = Assert.Single(report.Warnings);
            Assert.Equal(code, warning.Code);
            Assert.Equal(1, warning.SourceChannel);
            Assert.Equal(0, warning.SourceTrack);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.Contains("midiDuration=", warning.Original!);
            Assert.Contains("outputDuration=", warning.Converted!);
            if (mode == MidiPolyphonyMode.StealOldest)
            {
                Assert.Equal(0L, warning.SourceTick);
                Assert.Equal(0, warning.OutputTrack);
                Assert.Equal("outputTick=0, outputDuration=20", warning.Converted);
                Assert.Equal(150, tracks[0].Notes[1].EndTick);
            }
            else
            {
                Assert.Equal(20L, warning.SourceTick);
                Assert.Null(warning.OutputTrack);
                Assert.Equal("outputTick=20, outputDuration=0", warning.Converted);
            }
        }

        /// <summary>打ち切った音は新音終了後に再開せず、元の Off も新音を止めない。</summary>
        [Fact]
        public void TruncatedNoteNeverResumesAndOldOffDoesNotStopReplacement()
        {
            MidiVoiceNote first = MidiVoiceFixture.Note(60, endTick: 100);
            MidiVoiceNote second = MidiVoiceFixture.Note(64, startTick: 10, endTick: 150);
            MidiVoiceNote third = MidiVoiceFixture.Note(67, startTick: 150, endTick: 160);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first, second, third }, report,
                channelMap: MidiVoiceFixture.Map(1, 0));
            Assert.Equal(new[] { 10, 150, 160 }, tracks[0].Notes.Select(note => note.EndTick));
            Assert.Equal(new[] { 0, 10, 150 }, tracks[0].Notes.Select(note => note.StartTick));
            Assert.Single(report.Warnings);
        }

        /// <summary>最古開始を先に比較し、同開始では実効音量の小さい声を切る。</summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public void OldestStartPrecedesLowerVolume(int quietStart, int expectedTrack)
        {
            MidiVoiceNote loud = MidiVoiceFixture.Note(72, velocity: 127, startTick: 0);
            MidiVoiceNote quiet = MidiVoiceFixture.Note(60, velocity: 126, startTick: quietStart);
            MidiVoiceNote incoming = MidiVoiceFixture.Note(67, startTick: 10, endTick: 30);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { quiet, incoming, loud }, report,
                channelMap: MidiVoiceFixture.Map(1, 1, 0));
            Assert.Same(incoming, tracks[expectedTrack].Notes[1].Source);
            Assert.Equal(10, tracks[expectedTrack].Notes[0].EndTick);
            Assert.Equal(100, Assert.Single(tracks[1 - expectedTrack].Notes).EndTick);
        }

        /// <summary>既存音の開始・音量が同じなら最小出力番号を切る。</summary>
        [Fact]
        public void EqualOldestNotesStealLowestOutputTrack()
        {
            MidiVoiceNote first = MidiVoiceFixture.Note(60);
            MidiVoiceNote second = MidiVoiceFixture.Note(72);
            MidiVoiceNote incoming = MidiVoiceFixture.Note(67, startTick: 10, endTick: 30);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first, second, incoming }, MidiFileFixture.Report(),
                channelMap: MidiVoiceFixture.Map(1, 1, 0));
            Assert.Same(incoming, tracks[0].Notes[1].Source);
            Assert.Single(tracks[1].Notes);
        }

        /// <summary>同時刻に最初の新音が古い声を奪った後、その新音を後順位の新音で奪わない。</summary>
        [Fact]
        public void AcceptedSimultaneousOnCannotBeStolen()
        {
            MidiVoiceNote old = MidiVoiceFixture.Note(60);
            MidiVoiceNote high = MidiVoiceFixture.Note(72, startTick: 10, endTick: 20);
            MidiVoiceNote low = MidiVoiceFixture.Note(67, startTick: 10, endTick: 30);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { low, old, high }, report,
                channelMap: MidiVoiceFixture.Map(1, 0));
            Assert.Equal(new[] { old, high }, tracks[0].Notes.Select(note => note.Source));
            Assert.Equal(1L, report.WarningCountsByCode["NoteTruncated"]);
            Assert.Equal(1L, report.WarningCountsByCode["PolyphonyReduced"]);
        }

        /// <summary>明示 map で旋律と交差すると同時刻ドラムを先に採用する。</summary>
        [Fact]
        public void DrumPrecedesMelodyOnSharedExplicitSnesCandidate()
        {
            var range = new MidiPitchRange(0, 127);
            MidiVoiceNote melody = MidiVoiceFixture.Note(100, sampleRange: range);
            MidiVoiceNote drum = MidiVoiceFixture.Note(36, velocity: 1, channel: 10, sampleRange: range, drumPriority: MidiDrumPriority.Kick, outputPitch: 60);
            var map = new Dictionary<int, IReadOnlyList<int>> { [1] = new[] { 0 }, [10] = new[] { 0 } };
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.Snes);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Snes, new[] { melody, drum }, report, channelMap: map);
            Assert.Same(drum, Assert.Single(tracks[0].Notes).Source);
            Assert.Equal("PolyphonyReduced", Assert.Single(report.Warnings).Code);
        }

        /// <summary>分類済みドラムは kick→snare→tom→crash→openhat→hat の順で同時打撃を採用する。</summary>
        [Theory]
        [InlineData(MidiDrumPriority.Kick, MidiDrumPriority.Snare)]
        [InlineData(MidiDrumPriority.Snare, MidiDrumPriority.Tom)]
        [InlineData(MidiDrumPriority.Tom, MidiDrumPriority.Crash)]
        [InlineData(MidiDrumPriority.Crash, MidiDrumPriority.OpenHat)]
        [InlineData(MidiDrumPriority.OpenHat, MidiDrumPriority.Hat)]
        public void ClassifiedDrumsUsePriorityBeforeVolume(MidiDrumPriority earlier, MidiDrumPriority later)
        {
            MidiVoiceNote preferred = MidiVoiceFixture.Note(36, velocity: 1, channel: 10, drumPriority: earlier, outputPitch: 15);
            MidiVoiceNote rejected = MidiVoiceFixture.Note(42, channel: 10, drumPriority: later, outputPitch: 1);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { rejected, preferred }, MidiFileFixture.Report());
            Assert.Same(preferred, Assert.Single(tracks[3].Notes).Source);
        }

        /// <summary>後発ドラムも同じ polyphony 設定に従う。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest, 2)]
        [InlineData(MidiPolyphonyMode.DropNew, 1)]
        public void LaterDrumUsesConfiguredPolyphony(MidiPolyphonyMode mode, int count)
        {
            MidiVoiceNote first = MidiVoiceFixture.Note(36, channel: 10, drumPriority: MidiDrumPriority.Kick, outputPitch: 15);
            MidiVoiceNote second = MidiVoiceFixture.Note(42, channel: 10, startTick: 10, endTick: 20, drumPriority: MidiDrumPriority.Hat, outputPitch: 1);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first, second }, MidiFileFixture.Report(), mode);
            Assert.Equal(count, tracks[3].Notes.Count);
        }

        /// <summary>同じドラム分類では実効音量を優先し、同音量なら pitch より元イベント順を優先する。</summary>
        [Theory]
        [InlineData(127, 0)]
        [InlineData(126, 1)]
        public void SameDrumClassUsesVolumeThenSourceOrder(int firstVelocity, long expectedEvent)
        {
            string velocity = firstVelocity == 127 ? "7F" : "7E";
            IReadOnlyList<MidiNote> collected = MidiNoteCollectorTests.Collect(MidiFileFixture.Report(),
                MidiFileFixture.Bytes($"00 99 23 {velocity} 00 99 24 7F 0A FF 2F 00"));
            MidiVoiceNote[] notes = collected.Select(note => new MidiVoiceNote(new MidiQuantizedNote(note, 0, 10), 15,
                drumPriority: MidiDrumPriority.Kick)).Reverse().ToArray();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, notes, MidiFileFixture.Report());
            Assert.Equal(expectedEvent, Assert.Single(tracks[3].Notes).Source.Timing.Source.Source.SourceEvent);
        }

        /// <summary>重なりと同時 On を含む多声列は、全チップ・両モードで正の長さ・昇順・非重複になる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, MidiPolyphonyMode.StealOldest)]
        [InlineData(ChipKind.Nes, MidiPolyphonyMode.DropNew)]
        [InlineData(ChipKind.GameBoy, MidiPolyphonyMode.StealOldest)]
        [InlineData(ChipKind.GameBoy, MidiPolyphonyMode.DropNew)]
        [InlineData(ChipKind.Snes, MidiPolyphonyMode.StealOldest)]
        [InlineData(ChipKind.Snes, MidiPolyphonyMode.DropNew)]
        public void EveryOutputTrackHasPositiveOrderedNonOverlappingNotes(ChipKind chip, MidiPolyphonyMode mode)
        {
            const int NoteCount = 160;
            const int SimultaneousNotes = 5;
            var notes = new List<MidiVoiceNote>();
            var range = new MidiPitchRange(0, 127);
            for (int noteIndex = 0; noteIndex < NoteCount; noteIndex++)
            {
                int startTick = noteIndex / SimultaneousNotes;
                notes.Add(MidiVoiceFixture.Note(50 + noteIndex % 30, velocity: 1 + noteIndex % 127,
                    channel: 1 + noteIndex % 4, startTick: startTick, endTick: startTick + 1 + noteIndex % 11, sampleRange: range));
            }
            var report = new ConversionReport(ConversionFormat.Midi, chip);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(chip, notes, report, mode);
            foreach (MidiVoiceTrack track in tracks)
            {
                int previousEnd = 0;
                foreach (MidiAllocatedNote note in track.Notes)
                {
                    Assert.True(note.DurationTicks > 0);
                    Assert.True(note.StartTick >= previousEnd);
                    Assert.Equal(track.OutputTrack, note.OutputTrack);
                    Assert.True(note.EndTick <= note.Source.Timing.EndTick);
                    previousEnd = note.EndTick;
                }
            }
            Assert.Equal(NoteCount, report.Statistics["acceptedMidiNotes"] + report.Statistics["polyphonyNotesDropped"]);
            var repeatedReport = new ConversionReport(ConversionFormat.Midi, chip);
            IReadOnlyList<MidiVoiceTrack> repeated = MidiVoiceFixture.Allocate(chip, notes.AsEnumerable().Reverse().ToArray(), repeatedReport, mode);
            Assert.Equal(tracks.SelectMany(track => track.Notes).Select(note => (note.Source, note.OutputTrack, note.StartTick, note.EndTick)),
                repeated.SelectMany(track => track.Notes).Select(note => (note.Source, note.OutputTrack, note.StartTick, note.EndTick)));
        }

        /// <summary>短音延長で重なった隣接元音は割り当て時に解決し、各声を非重複に保つ。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest)]
        [InlineData(MidiPolyphonyMode.DropNew)]
        public void ExtendedGatesAreResolvedAfterQuantization(MidiPolyphonyMode mode)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(MidiFileFixture.Bytes(
                "01 90 3C 7F 01 80 3C 00 00 90 43 7F 01 80 43 00 00 FF 2F 00")), report);
            var options = new MidiImportOptions { Chip = ChipKind.Nes, Polyphony = mode, ChannelMap = MidiVoiceFixture.Map(1, 0) };
            MidiTickQuantizer quantizer = MidiTempoMapTests.CreateQuantizer(MidiTempoMapTests.CreateMap(file, options, report), options, report);
            MidiVoiceNote[] notes = MidiNoteCollector.Collect(file, report)!.Select(note =>
                new MidiVoiceNote(Assert.IsType<MidiQuantizedNote>(quantizer.QuantizeNote(note)), note.Pitch)).ToArray();
            IReadOnlyList<MidiVoiceTrack>? tracks = MidiVoiceAllocator.Allocate(notes, options, report);
            Assert.NotNull(tracks);
            Assert.Equal(67, Assert.Single(tracks[0].Notes).Pitch);
            Assert.Equal(1L, report.WarningCountsByCode["PolyphonyReduced"]);
            Assert.DoesNotContain(report.Warnings, warning => warning.Code == "NoteTruncated");
        }
    }
}
