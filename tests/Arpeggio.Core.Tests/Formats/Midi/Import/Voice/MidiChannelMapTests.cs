using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import.Voice;

namespace Arpeggio.Core.Tests.Formats.Midi.Import.Voice
{
    /// <summary>明示候補の検証・上書き・共有・除外を検証する。</summary>
    public sealed class MidiChannelMapTests
    {
        /// <summary>候補配列の順序は優先順位にならず、未指定 ch の自動候補を残す。</summary>
        [Fact]
        public void ExplicitOrderIsIgnoredAndOtherChannelsStayAutomatic()
        {
            MidiVoiceNote first = MidiVoiceFixture.Note(72, channel: 1);
            MidiVoiceNote second = MidiVoiceFixture.Note(67, channel: 2);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first, second }, report,
                channelMap: MidiVoiceFixture.Map(1, 2, 1));
            Assert.Same(first, Assert.Single(tracks[1].Notes).Source);
            Assert.Same(second, Assert.Single(tracks[0].Notes).Source);
            Assert.Empty(tracks[2].Notes);
        }

        /// <summary>複数 MIDI ch の候補共有は許可し、競合は通常の声規則で解決する。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest, 2)]
        [InlineData(MidiPolyphonyMode.DropNew, 1)]
        public void SharedCandidatesUseNormalPolyphony(MidiPolyphonyMode mode, int count)
        {
            var map = new Dictionary<int, IReadOnlyList<int>> { [1] = new[] { 0 }, [2] = new[] { 0 } };
            var notes = new[] { MidiVoiceFixture.Note(60, channel: 1), MidiVoiceFixture.Note(67, channel: 2, startTick: 10, endTick: 20) };
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, notes, MidiFileFixture.Report(), mode, map);
            Assert.Equal(count, tracks[0].Notes.Count);
        }

        /// <summary>空配列は明示除外として統計に残し、声不足警告には数えない。</summary>
        [Fact]
        public void EmptyMapEntryExcludesWithoutWarning()
        {
            var notes = new[] { MidiVoiceFixture.Note(channel: 1), MidiVoiceFixture.Note(channel: 2) };
            var map = new Dictionary<int, IReadOnlyList<int>> { [1] = Array.Empty<int>(), [16] = Array.Empty<int>() };
            ConversionReport report = MidiFileFixture.Report(strict: true);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, notes, report, channelMap: map);
            Assert.Equal(2, Assert.Single(tracks[0].Notes).Source.Timing.Source.Source.Channel);
            Assert.Equal(2L, report.Statistics["explicitlyExcludedMidiChannels"]);
            Assert.Equal(1L, report.Statistics["explicitlyExcludedMidiNotes"]);
            Assert.Empty(report.Warnings);
            Assert.True(report.CanWrite);
        }

        /// <summary>全音除外と空入力は NoPlayableNotes とし、空曲を成功として返さない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NoRemainingNotesIsAnError(bool emptyInput)
        {
            MidiVoiceNote[] notes = emptyInput ? Array.Empty<MidiVoiceNote>() : new[] { MidiVoiceFixture.Note() };
            ConversionReport report = MidiFileFixture.Report();
            Assert.Null(MidiVoiceAllocator.Allocate(notes,
                new MidiImportOptions { Chip = ChipKind.Nes, ChannelMap = MidiVoiceFixture.Map(1) }, report));
            Assert.Equal("NoPlayableNotes", Assert.Single(report.Errors).Code);
            Assert.Empty(report.Warnings);
        }

        /// <summary>キー・範囲・重複・DPCM・旋律 Noise 相互指定は未使用 ch でも拒否する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 0, 0, 0)]
        [InlineData(ChipKind.Nes, 17, 0, 0)]
        [InlineData(ChipKind.Nes, 1, -1, 0)]
        [InlineData(ChipKind.Nes, 1, 5, 0)]
        [InlineData(ChipKind.Nes, 1, 0, 0)]
        [InlineData(ChipKind.Nes, 1, 4, 0)]
        [InlineData(ChipKind.Nes, 10, 4, 3)]
        [InlineData(ChipKind.Nes, 1, 3, 0)]
        [InlineData(ChipKind.Nes, 10, 0, 3)]
        [InlineData(ChipKind.GameBoy, 1, 4, 0)]
        [InlineData(ChipKind.GameBoy, 1, 3, 0)]
        [InlineData(ChipKind.GameBoy, 10, 2, 3)]
        [InlineData(ChipKind.Snes, 1, 8, 0)]
        [InlineData(ChipKind.Snes, 1, 0, 0)]
        public void InvalidEntriesRejectTheWholeAllocation(ChipKind chip, int channel, int first, int second)
        {
            var report = new ConversionReport(ConversionFormat.Midi, chip);
            MidiVoiceNote note = MidiVoiceFixture.Note(sampleRange: new MidiPitchRange(0, 127));
            Assert.Null(MidiVoiceAllocator.Allocate(new[] { note },
                new MidiImportOptions { Chip = chip, ChannelMap = MidiVoiceFixture.Map(channel, first, second) }, report));
            Assert.Equal("InvalidChannelMap", Assert.Single(report.Errors).Code);
            Assert.Empty(report.Warnings);
        }

        /// <summary>null 候補と不正なモードは操作エラーとして戻す。</summary>
        [Fact]
        public void NullCandidatesAndUnknownPolyphonyAreErrors()
        {
            MidiVoiceNote note = MidiVoiceFixture.Note();
            ConversionReport mapReport = MidiFileFixture.Report();
            var map = new Dictionary<int, IReadOnlyList<int>> { [1] = null! };
            Assert.Null(MidiVoiceAllocator.Allocate(new[] { note }, new MidiImportOptions { Chip = ChipKind.Nes, ChannelMap = map }, mapReport));
            Assert.Equal("InvalidChannelMap", Assert.Single(mapReport.Errors).Code);
            foreach (MidiPolyphonyMode mode in new[] { MidiPolyphonyMode.None, (MidiPolyphonyMode)99 })
            {
                ConversionReport report = MidiFileFixture.Report();
                Assert.Null(MidiVoiceAllocator.Allocate(new[] { note }, new MidiImportOptions { Chip = ChipKind.Nes, Polyphony = mode }, report));
                Assert.Equal("InvalidOptions", Assert.Single(report.Errors).Code);
            }
        }

        /// <summary>SNES のドラム予約判定は明示除外前の有効 On を用いる。</summary>
        [Fact]
        public void ExplicitDrumExclusionDoesNotRewriteAutomaticMelodyCandidates()
        {
            var range = new MidiPitchRange(0, 127);
            var notes = Enumerable.Range(60, 8).Select(pitch => MidiVoiceFixture.Note(pitch, sampleRange: range)).ToList();
            notes.Add(MidiVoiceFixture.Note(36, channel: 10, sampleRange: range, drumPriority: MidiDrumPriority.Kick, outputPitch: 60));
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.Snes);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Snes, notes, report, channelMap: MidiVoiceFixture.Map(10));
            Assert.Equal(6, tracks.Sum(track => track.Notes.Count));
            Assert.Empty(tracks[6].Notes);
            Assert.Empty(tracks[7].Notes);
            Assert.Equal(1L, report.Statistics["explicitlyExcludedMidiNotes"]);
        }

        /// <summary>先行エラーは処理を止め、strict 警告は残りの割り当て診断まで継続する。</summary>
        [Fact]
        public void ExistingErrorStopsAndStrictWarningDoesNotStopAllocation()
        {
            var notes = new[] { MidiVoiceFixture.Note(60), MidiVoiceFixture.Note(64), MidiVoiceFixture.Note(67), MidiVoiceFixture.Note(71) };
            ConversionReport strictReport = MidiFileFixture.Report(strict: true);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, notes, strictReport);
            Assert.Equal(3, tracks.Sum(track => track.Notes.Count));
            Assert.False(strictReport.CanWrite);
            strictReport.AddError(new ConversionDiagnostic("PreviousError", "先行エラー。"));
            long warningsBefore = strictReport.WarningCount;
            Assert.Null(MidiVoiceAllocator.Allocate(notes, new MidiImportOptions { Chip = ChipKind.Nes }, strictReport));
            Assert.Equal(warningsBefore, strictReport.WarningCount);
        }
    }
}
