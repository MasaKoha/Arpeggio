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
    /// <summary>チップ別候補と同時発音の決定性を確定 gate から検証する。</summary>
    public sealed class MidiVoiceAllocatorTests
    {
        /// <summary>NES の四和音は B / G / E を採用し C を捨て、Noise と DPCM は空にする。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest)]
        [InlineData(MidiPolyphonyMode.DropNew)]
        public void NesFourNoteChordMatchesFixedAllocation(MidiPolyphonyMode polyphony)
        {
            var notes = new[] { MidiVoiceFixture.Note(60), MidiVoiceFixture.Note(64), MidiVoiceFixture.Note(67), MidiVoiceFixture.Note(71) };
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, notes, report, polyphony);
            Assert.Equal(5, tracks.Count);
            Assert.Equal(71, Assert.Single(tracks[0].Notes).Pitch);
            Assert.Equal(67, Assert.Single(tracks[1].Notes).Pitch);
            Assert.Equal(64, Assert.Single(tracks[2].Notes).Pitch);
            Assert.Equal(15, Assert.Single(tracks[2].Notes).Volume);
            Assert.Empty(tracks[3].Notes);
            Assert.Empty(tracks[4].Notes);
            Assert.Equal(new[] { 0, 1, 0, 0, 0 }, tracks.Select(track => track.ChannelIndex));
            Assert.Equal(1L, report.WarningCountsByCode["PolyphonyReduced"]);
            Assert.Equal(1L, report.WarningCountsByCode["TriangleVolumeIgnored"]);
            Assert.DoesNotContain(report.Warnings, warning => warning.Code == "NoteTruncated");
            Assert.Equal(3L, report.Statistics["acceptedMidiNotes"]);
            Assert.Equal(1L, report.Statistics["midiChannel.1.outputTrack.2.notes"]);
        }

        /// <summary>GB の旋律候補は Pulse / Pulse / Wave で、ドラムだけを Noise に送る。</summary>
        [Fact]
        public void GameBoyCandidatesKeepMelodyAndNoiseSeparate()
        {
            var notes = new[]
            {
                MidiVoiceFixture.Note(60), MidiVoiceFixture.Note(64), MidiVoiceFixture.Note(67),
                MidiVoiceFixture.Note(36, channel: 10, drumPriority: MidiDrumPriority.Kick, outputPitch: 72)
            };
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.GameBoy);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.GameBoy, notes, report);
            Assert.Equal(new[] { ChannelKind.Pulse, ChannelKind.Pulse, ChannelKind.Wave, ChannelKind.Noise }, tracks.Select(track => track.Channel));
            Assert.Equal(new[] { 67, 64, 60, 72 }, tracks.Select(track => Assert.Single(track.Notes).Pitch));
            Assert.All(tracks, track => Assert.Equal(12, Assert.Single(track.Notes).Volume));
            Assert.Empty(report.Warnings);
        }

        /// <summary>SNES はドラムがある時だけ 6 / 7 を予約し、それ以外は全 8 声を旋律に使う。</summary>
        [Theory]
        [InlineData(false, 8, 0)]
        [InlineData(true, 6, 2)]
        public void SnesReservesTwoVoicesOnlyWhenDrumsExist(bool hasDrums, int melodyCount, int droppedCount)
        {
            var range = new MidiPitchRange(0, 127);
            var notes = Enumerable.Range(60, 8).Select(pitch => MidiVoiceFixture.Note(pitch, sampleRange: range)).ToList();
            if (hasDrums)
            {
                notes.Add(MidiVoiceFixture.Note(36, channel: 10, sampleRange: range, drumPriority: MidiDrumPriority.Kick, outputPitch: 60));
                notes.Add(MidiVoiceFixture.Note(38, channel: 10, sampleRange: range, drumPriority: MidiDrumPriority.Snare, outputPitch: 60));
            }
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.Snes);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Snes, notes, report);
            Assert.Equal(8, tracks.Count);
            Assert.Equal(melodyCount, tracks.SelectMany(track => track.Notes).Count(note => !note.Source.Timing.Source.IsDrum));
            Assert.Equal(droppedCount, report.Statistics["polyphonyNotesDropped"]);
            Assert.Equal(hasDrums, Assert.Single(tracks[6].Notes).Source.Timing.Source.IsDrum);
            Assert.Equal(hasDrums, Assert.Single(tracks[7].Notes).Source.Timing.Source.IsDrum);
            Assert.Equal(Enumerable.Range(0, 8), tracks.Select(track => track.ChannelIndex));
        }

        /// <summary>4 bit 音量が同じでも変換前実効音量を先に比較し、その後に pitch を比較する。</summary>
        [Fact]
        public void EffectiveVolumePrecedesPitchBeforeFourBitRounding()
        {
            MidiVoiceNote louder = MidiVoiceFixture.Note(60, velocity: 127);
            MidiVoiceNote higher = MidiVoiceFixture.Note(72, velocity: 126);
            Assert.Equal(louder.Timing.Source.Volume, higher.Timing.Source.Volume);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { higher, louder }, report);
            Assert.Same(louder, Assert.Single(tracks[0].Notes).Source);
            Assert.Same(higher, Assert.Single(tracks[1].Notes).Source);
        }

        /// <summary>音量と pitch が同じなら ch → 元 MTrk → 元 event の順で入力列の並びに依存しない。</summary>
        [Fact]
        public void EqualNotesUseChannelTrackAndEventTieBreakers()
        {
            IReadOnlyList<MidiNote> collected = MidiNoteCollectorTests.Collect(MidiFileFixture.Report(),
                MidiFileFixture.Bytes("00 91 3C 7F 00 90 3C 7F 00 90 3C 7F 0A FF 2F 00"),
                MidiFileFixture.Bytes("00 90 3C 7F 0A FF 2F 00"));
            MidiVoiceNote[] notes = collected.Select(note => new MidiVoiceNote(new MidiQuantizedNote(note, 0, 10), note.Pitch)).ToArray();
            for (int iteration = 0; iteration < notes.Length; iteration++)
            {
                MidiVoiceNote[] shuffled = notes.Skip(iteration).Concat(notes.Take(iteration)).Reverse().ToArray();
                IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, shuffled, MidiFileFixture.Report());
                Assert.Equal(1L, Assert.Single(tracks[0].Notes).Source.Timing.Source.Source.SourceEvent);
                Assert.Equal(2L, Assert.Single(tracks[1].Notes).Source.Timing.Source.Source.SourceEvent);
                Assert.Equal(1, Assert.Single(tracks[2].Notes).Source.Timing.Source.Source.SourceTrack);
            }
        }

        /// <summary>開始順は実効音量より優先し、終了済みの最小候補を再利用する。</summary>
        [Fact]
        public void EarlierOnsetWinsAndAdjacentNotesReleaseBeforeOn()
        {
            MidiVoiceNote earlier = MidiVoiceFixture.Note(60, velocity: 1, startTick: 0, endTick: 10);
            MidiVoiceNote later = MidiVoiceFixture.Note(72, startTick: 10, endTick: 20);
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { later, earlier }, report,
                channelMap: MidiVoiceFixture.Map(1, 1, 0));
            Assert.Equal(new[] { earlier, later }, tracks[0].Notes.Select(note => note.Source));
            Assert.Empty(tracks[1].Notes);
            Assert.Empty(report.Warnings);
        }

        /// <summary>返却列は独立した読み取り専用列で、再割り当て時も入力 gate を変更しない。</summary>
        [Fact]
        public void ResultsAndInputsRemainIndependentAndImmutable()
        {
            MidiVoiceNote first = MidiVoiceFixture.Note(endTick: 100);
            MidiVoiceNote second = MidiVoiceFixture.Note(startTick: 20, endTick: 40);
            var candidates = new[] { 0 };
            var map = MidiVoiceFixture.Map(1, candidates);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first, second }, MidiFileFixture.Report(), channelMap: map);
            Assert.Equal(100, first.Timing.EndTick);
            Assert.Equal(20, tracks[0].Notes[0].EndTick);
            candidates[0] = 1;
            Assert.Equal(2, tracks[0].Notes.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<MidiVoiceTrack>)tracks).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<MidiAllocatedNote>)tracks[0].Notes).Clear());
            IReadOnlyList<MidiVoiceTrack> repeated = MidiVoiceFixture.Allocate(ChipKind.Nes, new[] { first }, MidiFileFixture.Report());
            Assert.Equal(100, Assert.Single(repeated[0].Notes).EndTick);
        }
    }
}
