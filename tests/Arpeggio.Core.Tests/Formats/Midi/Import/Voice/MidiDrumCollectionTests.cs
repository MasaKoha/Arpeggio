using System.Collections.Generic;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Midi.Import.Voice
{
    /// <summary>固定 gate を後段で決める打楽器の元 gate と CC120 上限を検証する。</summary>
    public sealed class MidiDrumCollectionTests
    {
        /// <summary>打楽器の不明 Off / 未終了は旋律用の警告から除き、元 Off の有無を残す。</summary>
        [Fact]
        public void DrumOffWarningsAreSuppressedAndMissingGateIsPreserved()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 89 24 00 00 99 24 7F 01 99 26 7F 01 89 24 00 03 FF 2F 00"));
            Assert.All(notes, note => Assert.True(note.IsDrum));
            Assert.Equal(new long?[] { 2, null }, notes.Select(note => note.KeyOffTick));
            Assert.Equal(new long[] { 2, 5 }, notes.Select(note => note.EndTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>元 gate が 0 でも固定 gate 用 On は維持する。</summary>
        [Fact]
        public void ZeroSourceDrumGateIsKeptForFixedGateMapping()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiNote note = Assert.Single(MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 99 24 7F 00 89 24 00 01 FF 2F 00")));
            Assert.Equal(0L, note.EndTick);
            Assert.Equal(0L, note.KeyOffTick);
            Assert.Empty(report.Warnings);
        }

        /// <summary>pedal は打楽器の元 Off を遅らせず、CC123 も比較用のキー Off として保持する。</summary>
        [Fact]
        public void SustainAndAllNotesOffDoNotCreateDrumSustainGate()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 B9 40 7F 00 99 24 7F 00 99 26 7F 01 89 24 00 01 B9 7B 00 03 B9 40 00 00 FF 2F 00"));
            Assert.Equal(new long[] { 1, 2 }, notes.Select(note => note.EndTick));
            Assert.Equal(new long?[] { 1, 2 }, notes.Select(note => note.KeyOffTick));
            Assert.All(notes, note => Assert.Null(note.SoundOffTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>元 Off の後に届く CC120 も固定 gate の上限として保持し、後の CC120 で上書きしない。</summary>
        [Fact]
        public void AllSoundOffAfterSourceOffStillLimitsDrumGate()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 99 24 7F 01 89 24 00 01 B9 78 00 00 99 26 7F 01 B9 78 00 02 FF 2F 00"));
            Assert.Equal(new long?[] { 2, 3 }, notes.Select(note => note.SoundOffTick));
            Assert.Equal(new long?[] { 1, null }, notes.Select(note => note.KeyOffTick));
            Assert.Equal(new long[] { 1, 3 }, notes.Select(note => note.EndTick));
            Assert.Empty(report.Warnings);
        }

        /// <summary>On と同順序時刻の CC120 は固定 gate も 0 にするため破棄する。</summary>
        [Fact]
        public void ImmediateAllSoundOffDropsDrumOnset()
        {
            ConversionReport report = MidiFileFixture.Report();
            Assert.Empty(MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 99 24 7F 00 B9 78 00 01 FF 2F 00")));
            Assert.Equal("ZeroLengthNoteDropped", Assert.Single(report.Warnings).Code);
        }
    }
}
