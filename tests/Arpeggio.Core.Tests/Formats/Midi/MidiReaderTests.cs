using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats.Midi
{
    /// <summary>手作り SMF の固定値からヘッダー、メッセージ長、元位置と所有権を検証する。</summary>
    public sealed class MidiReaderTests
    {
        /// <summary>format 0 / 1 と PPQN の両端・代表値を受理する。</summary>
        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 480)]
        [InlineData(0, 32767)]
        [InlineData(1, 1)]
        [InlineData(1, 480)]
        [InlineData(1, 32767)]
        public void HeaderValuesAreAccepted(int format, int division)
        {
            byte[] bytes = MidiFileFixture.Create(format, division, MidiFileFixture.Bytes("00 90 3C 64 01 80 3C 00 00 FF 2F 00"));
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(bytes, report);
            Assert.Equal(format, file.Format);
            Assert.Equal(division, file.TicksPerQuarterNote);
            Assert.Equal(1, file.TrackCount);
            Assert.Equal(1L, file.EndTick);
            Assert.Equal(3, file.Events.Count);
            Assert.Equal(0.5 / division, report.DurationSeconds, 12);
            Assert.Equal(bytes.Length, report.Statistics["midiInputBytes"]);
            Assert.True(report.CanWrite);
        }

        /// <summary>7 種類すべての channel message は通常・running status の双方で正しい長さを読む。</summary>
        [Theory]
        [InlineData("80", "3C 40", MidiMessageKind.NoteOff, 60, 64)]
        [InlineData("90", "3C 40", MidiMessageKind.NoteOn, 60, 64)]
        [InlineData("A0", "3C 40", MidiMessageKind.PolyphonicPressure, 60, 64)]
        [InlineData("B0", "07 40", MidiMessageKind.ControlChange, 7, 64)]
        [InlineData("C0", "40", MidiMessageKind.ProgramChange, 64, 0)]
        [InlineData("D0", "40", MidiMessageKind.ChannelPressure, 64, 0)]
        [InlineData("E0", "00 40", MidiMessageKind.PitchBend, 0, 64)]
        public void ChannelLengthsAndRunningStatusAreRecognized(string status, string payload, MidiMessageKind kind, int dataOne, int dataTwo)
        {
            byte[] track = MidiFileFixture.Bytes($"00 {status} {payload} 01 {payload} 00 FF 2F 00");
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(track), MidiFileFixture.Report());
            Assert.Equal(3, file.Events.Count);
            for (int eventIndex = 0; eventIndex < 2; eventIndex++)
            {
                MidiEvent current = file.Events[eventIndex];
                Assert.Equal(kind, current.Kind);
                Assert.Equal(1, current.Channel);
                Assert.Equal(dataOne, current.DataOne);
                Assert.Equal(dataTwo, current.DataTwo);
                Assert.Equal(eventIndex, current.SourceEvent);
                Assert.Equal(eventIndex, current.Tick);
            }
        }

        /// <summary>全チャンネルの 1 始まり表記と velocity 0 の元メッセージを維持する。</summary>
        [Fact]
        public void ChannelNumbersAndVelocityZeroArePreserved()
        {
            using var track = new MemoryStream();
            for (int channel = 0; channel < 16; channel++)
            {
                track.Write(new byte[] { 0, (byte)(0x90 + channel), 60, 0 });
            }
            track.Write(MidiFileFixture.Bytes("00 FF 2F 00"));
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(track.ToArray()), report);
            Assert.Equal(Enumerable.Range(1, 16), file.Events.Take(16).Select(current => current.Channel));
            Assert.All(file.Events.Take(16), current => Assert.Equal(MidiMessageKind.NoteOn, current.Kind));
            Assert.Equal(0L, report.Statistics["midiNoteOns"]);
        }

        /// <summary>元イベント番号は meta / SysEx も数え、同 tick は元トラック・元イベント順に固定する。</summary>
        [Fact]
        public void TracksMergeInSourceOrderAndLatestEndWins()
        {
            byte[] first = MidiFileFixture.Bytes("00 FF 01 00 02 91 3C 40 00 C1 05 00 FF 2F 00");
            byte[] second = MidiFileFixture.Bytes("00 F0 02 7F F7 02 81 3C 00 03 FF 2F 00");
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(first, second), report);
            Assert.Equal(new[] { MidiMessageKind.NoteOn, MidiMessageKind.ProgramChange, MidiMessageKind.EndOfTrack,
                MidiMessageKind.NoteOff, MidiMessageKind.EndOfTrack }, file.Events.Select(current => current.Kind));
            Assert.Equal(new long[] { 1, 2, 3, 1, 2 }, file.Events.Select(current => current.SourceEvent));
            Assert.Equal(5L, file.EndTick);
            Assert.Equal(7L, report.Statistics["midiEvents"]);
            Assert.Contains(report.Warnings, diagnostic => diagnostic.Code == "SysExIgnored" && diagnostic.SourceTrack == 1 && diagnostic.SourceEvent == 0);
        }

        /// <summary>ヘッダー拡張と未知チャンクを検証して読み飛ばす。</summary>
        [Fact]
        public void HeaderExtensionAndUnknownChunksAreAccepted()
        {
            byte[] bytes = MidiFileFixture.Bytes("4D546864 00000008 0000 0001 01E0 AABB 4A554E4B 00000002 CCDD 4D54726B 00000004 00FF2F00 4A554E4B 00000000");
            ConversionReport report = MidiFileFixture.Report(strict: true);
            MidiFile file = MidiFileFixture.Read(bytes, report);
            Assert.Single(file.Events);
            Assert.Equal(2L, report.WarningCountsByCode["UnknownMidiChunkIgnored"]);
            Assert.False(report.CanWrite);
        }

        /// <summary>4 byte VLQ の最大値と 32 bit を超える絶対 tick を保持する。</summary>
        [Fact]
        public void MaximumVariableLengthAndLongTicksAreAccepted()
        {
            using var track = new MemoryStream();
            track.Write(MidiFileFixture.Bytes("00 FF 51 03 000001"));
            for (int eventIndex = 0; eventIndex < 9; eventIndex++)
            {
                track.Write(MidiFileFixture.Bytes("FFFFFF7F C0 00"));
            }
            track.Write(MidiFileFixture.Bytes("00 FF 2F 00"));
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(0, 32767, track.ToArray()), MidiFileFixture.Report());
            Assert.Equal(2415919095L, file.EndTick);
        }

        /// <summary>固定長 meta と Tempo の最大値・port 0 を受理する。</summary>
        [Fact]
        public void KnownFixedMetadataLengthsAreAccepted()
        {
            byte[] track = MidiFileFixture.Bytes("00 FF 00 02 0001 00 FF 20 01 00 00 FF 21 01 00 00 FF 51 03 FFFFFF 00 FF 54 05 0000000000 00 FF 58 04 04021808 00 FF 59 02 0000 00 FF 2F 00");
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(track), MidiFileFixture.Report());
            Assert.Equal(16777215, file.Events[0].DataOne);
        }

        /// <summary>タイトル候補は track 0 の最初の非空名だけを UTF-8、失敗時 Latin-1 で保持する。</summary>
        [Theory]
        [InlineData("E69BB2", "曲", false)]
        [InlineData("E9", "é", true)]
        public void TrackNameUsesSpecifiedDecoding(string encoded, string expected, bool fallback)
        {
            byte[] name = MidiFileFixture.Bytes(encoded);
            using var track = new MemoryStream();
            track.Write(MidiFileFixture.Bytes("00 FF 03 00 00 FF 03"));
            track.WriteByte((byte)name.Length);
            track.Write(name);
            track.Write(MidiFileFixture.Bytes("00 FF 03 01 58 00 FF 2F 00"));
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(track.ToArray(), MidiFileFixture.Bytes("00 FF 03 01 59 00 FF 2F 00")), report);
            Assert.Equal(expected, file.TrackName);
            Assert.Equal(fallback, report.Warnings.Any(diagnostic => diagnostic.Code == "MidiTextDecoded"));
        }

        /// <summary>短い読み取り・シーク不能の Stream を閉じず、完成列への外部変更を拒否する。</summary>
        [Fact]
        public void FragmentedStreamAndImmutableEventsAreSupported()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 03 03 E69BB2 00 F7 03 010203 00 FF 2F 00"));
            using var stream = new MidiFragmentedStream(bytes);
            MidiFile? file = MidiReader.Read(stream, MidiFileFixture.Report());
            Assert.NotNull(file);
            Assert.True(stream.CanRead);
            Assert.Equal("曲", file.TrackName);
            Assert.Throws<NotSupportedException>(() => ((IList<MidiEvent>)file.Events).Clear());
            Array.Clear(bytes);
            Assert.Equal(MidiMessageKind.EndOfTrack, Assert.Single(file.Events).Kind);
        }

        /// <summary>下位 Stream の I/O 失敗を不正 MIDI 診断にすり替えない。</summary>
        [Fact]
        public void InputOutputFailurePropagatesWithoutClosingStream()
        {
            using var stream = new MidiFragmentedStream(MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00")), failAfterBytes: 23);
            ConversionReport report = MidiFileFixture.Report();
            Assert.Throws<IOException>(() => MidiReader.Read(stream, report));
            Assert.Empty(report.Errors);
            Assert.True(stream.CanRead);
        }
    }
}
