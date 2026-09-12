using System.Collections.Generic;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Midi.Import.Voice
{
    /// <summary>sustain と CC120 / 123 / 121 の異なる停止規則を固定 tick で検証する。</summary>
    public sealed class MidiSustainTests
    {
        /// <summary>CC64 は 64 以上だけで保持し、再打鍵を独立した FIFO の On として扱う。</summary>
        [Theory]
        [InlineData(63, 2, 4)]
        [InlineData(64, 6, 6)]
        [InlineData(127, 6, 6)]
        public void SustainThresholdAndRepeatedKeysArePreserved(int pedalValue, long firstEnd, long secondEnd)
        {
            ConversionReport report = MidiFileFixture.Report();
            byte[] track = MidiFileFixture.Bytes($"00 B0 40 {pedalValue:X2} 00 90 3C 7F 02 80 3C 00 01 90 3C 40 01 80 3C 00 02 B0 40 00 00 FF 2F 00");
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report, track);
            Assert.Equal(new[] { firstEnd, secondEnd }, notes.Select(note => note.EndTick));
            Assert.Equal(new long?[] { 2, 4 }, notes.Select(note => note.KeyOffTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>pedal の解除は解放済みキーだけを止め、押したままのキーは Off を待つ。</summary>
        [Fact]
        public void PedalUpDoesNotReleaseHeldKeys()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 B0 40 7F 00 90 3C 7F 00 90 40 7F 02 80 3C 00 02 B0 40 00 02 80 40 00 00 FF 2F 00"));
            Assert.Equal(new long[] { 4, 6 }, notes.Select(note => note.EndTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>別 MTrk の pedal 状態で保持し、曲の最遅 EOT で解除する。</summary>
        [Fact]
        public void SustainCrossesTracksAndClosesAtGlobalEnd()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 B0 40 7F 01 FF 2F 00"),
                MidiFileFixture.Bytes("00 90 3C 7F 02 80 3C 00 03 FF 2F 00"),
                MidiFileFixture.Bytes("0A FF 2F 00"));
            MidiNote note = Assert.Single(notes);
            Assert.Equal(10L, note.EndTick);
            Assert.Equal(2L, note.KeyOffTick);
            Assert.Equal("UnclosedNote", Assert.Single(report.Warnings).Code);
        }

        /// <summary>CC120 は pedal と押下キーを即停止し、他チャンネルを停止しない。</summary>
        [Fact]
        public void AllSoundOffStopsChannelAndKeepsPedalState()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 B0 40 7F 00 90 3C 7F 00 90 40 7F 00 91 43 7F 01 80 3C 00 02 B0 78 00 01 90 3E 7F 01 80 3E 00 02 B0 40 00 01 81 43 00 00 FF 2F 00"));
            Assert.Equal(new long[] { 3, 3, 8, 7 }, notes.Select(note => note.EndTick));
            Assert.Equal(new long?[] { 3, 3, null, null }, notes.Select(note => note.SoundOffTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>CC123 は全キーの Off として同音の全 On を解放し、pedal を適用する。</summary>
        [Theory]
        [InlineData(0, 3)]
        [InlineData(127, 5)]
        public void AllNotesOffHonorsSustain(int pedalValue, long expectedEnd)
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes($"00 B0 40 {pedalValue:X2} 00 90 3C 7F 01 90 3C 40 02 B0 7B 00 02 B0 40 00 00 FF 2F 00"));
            Assert.Equal(2, notes.Count);
            Assert.All(notes, note => Assert.Equal(expectedEnd, note.EndTick));
            Assert.All(notes, note => Assert.Equal(3L, note.KeyOffTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>CC121 は保持を解除し CC7 / CC11 を戻すが、Program と押下キーは維持する。</summary>
        [Fact]
        public void ResetControllersRestoresDefaultsAndPreservesProgram()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 C0 05 00 B0 07 40 00 B0 0B 40 00 B0 40 7F 00 90 3C 7F 00 90 40 7F 01 80 3C 00 02 B0 79 00 00 90 43 7F 02 80 40 00 00 80 43 00 00 FF 2F 00"));
            Assert.Equal(new long[] { 3, 5, 5 }, notes.Select(note => note.EndTick));
            Assert.Equal(new[] { 4, 4, 12 }, notes.Select(note => note.Volume));
            Assert.All(notes, note => Assert.Equal(5, note.Program));
            Assert.Equal(100, notes[2].ChannelVolume);
            Assert.Equal(127, notes[2].Expression);
            Assert.Equal("ControllerDuringNoteIgnored", Assert.Single(report.Warnings).Code);
        }

        /// <summary>同 tick の pedal と Off の元順序を保つ。</summary>
        [Theory]
        [InlineData("00 B0 40 7F 00 80 3C 00", 4)]
        [InlineData("00 80 3C 00 00 B0 40 7F", 2)]
        public void SameTickPedalOrderControlsGate(string events, long expectedEnd)
        {
            // 先頭の delta を 2 に置き換え、両イベントを tick 2 に置く。
            byte[] middle = MidiFileFixture.Bytes(events);
            middle[0] = 2;
            byte[] track = MidiFileFixture.Bytes("00 90 3C 7F").Concat(middle).Concat(MidiFileFixture.Bytes("02 B0 40 00 00 FF 2F 00")).ToArray();
            ConversionReport report = MidiFileFixture.Report();
            Assert.Equal(expectedEnd, Assert.Single(MidiNoteCollectorTests.Collect(report, track)).EndTick);
            Assert.Empty(report.Warnings);
        }
    }
}
