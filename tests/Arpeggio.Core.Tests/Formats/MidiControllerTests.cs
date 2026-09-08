using System.Collections.Generic;
using System.Linq;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>On 時点の音色・音量と、無視するコントローラーの診断条件を検証する。</summary>
    public sealed class MidiControllerTests
    {
        /// <summary>初期音量と velocity の 4 bit 化は固定値・最低 1 を維持する。</summary>
        [Theory]
        [InlineData(1, 1)]
        [InlineData(64, 6)]
        [InlineData(127, 12)]
        public void VelocityUsesDefaultChannelVolume(int velocity, int volume)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiNote note = Assert.Single(MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes($"00 90 3C {velocity:X2} 01 80 3C 00 00 FF 2F 00")));
            Assert.Equal(volume, note.Volume);
            Assert.Equal(velocity, note.Velocity);
            Assert.Equal(100, note.ChannelVolume);
            Assert.Equal(127, note.Expression);
            Assert.Equal(velocity / 127.0 * 100 / 127, note.EffectiveVolume, 12);
            Assert.Empty(report.Warnings);
        }

        /// <summary>CC7 と CC11 の両方を乗算し、正の最小積も発音を残す。</summary>
        [Theory]
        [InlineData(127, 127, 15)]
        [InlineData(64, 64, 4)]
        [InlineData(1, 1, 1)]
        public void VolumeAndExpressionAreMultiplied(int channelVolume, int expression, int expectedVolume)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiNote note = Assert.Single(MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes($"00 B0 07 {channelVolume:X2} 00 B0 0B {expression:X2} 00 90 3C 7F 01 80 3C 00 00 FF 2F 00")));
            Assert.Equal(expectedVolume, note.Volume);
            Assert.Empty(report.Warnings);
        }

        /// <summary>無音 On も FIFO 対応から除かず、後続の有音 On を古い Off で停止しない。</summary>
        [Theory]
        [InlineData("07")]
        [InlineData("0B")]
        public void SilentNoteParticipatesInFifoBeforeBeingOmitted(string controller)
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes($"00 B0 {controller} 00 00 90 3C 7F 01 B0 {controller} 7F 00 90 3C 7F 01 80 3C 00 02 80 3C 00 00 FF 2F 00"));
            MidiNote note = Assert.Single(notes);
            Assert.Equal(1L, note.StartTick);
            Assert.Equal(4L, note.EndTick);
            Assert.Equal(1L, report.Statistics["zeroVolumeNotesDropped"]);
            Assert.Empty(report.Warnings);
        }

        /// <summary>同 tick の Program / CC は元順序で評価し、既に始まった音には遡及しない。</summary>
        [Fact]
        public void SameTickStateIsCapturedAtEachOnsetAcrossTracks()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 C0 05 00 90 3C 7F 00 B0 07 40 00 C0 09 01 80 3C 00 00 FF 2F 00"),
                MidiFileFixture.Bytes("00 90 40 7F 01 80 40 00 00 FF 2F 00"));
            Assert.Equal(new[] { 5, 9 }, notes.Select(note => note.Program));
            Assert.Equal(new[] { 12, 8 }, notes.Select(note => note.Volume));
            ConversionDiagnostic warning = Assert.Single(report.Warnings);
            Assert.Equal("ControllerDuringNoteIgnored", warning.Code);
            Assert.Equal(2L, warning.SourceEvent);
            Assert.Equal(0, warning.SourceTrack);
            Assert.Equal(0L, warning.SourceTick);
        }

        /// <summary>同値再送、発音前、終端同 tick、ゼロ長の CC は無視した音量変更と誤診断しない。</summary>
        [Theory]
        [InlineData("00 B0 07 40 00 90 3C 7F 01 80 3C 00")]
        [InlineData("00 90 3C 7F 01 B0 07 64 01 80 3C 00")]
        [InlineData("00 90 3C 7F 01 B0 07 40 00 80 3C 00")]
        [InlineData("00 90 3C 7F 00 B0 07 40 00 80 3C 00")]
        [InlineData("00 B0 07 00 00 90 3C 7F 01 B0 07 40 01 80 3C 00")]
        public void VolumeDiagnosticsUseCompletedAudibleIntervals(string events)
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiNoteCollectorTests.Collect(report, MidiFileFixture.Bytes(events + " 00 FF 2F 00"));
            Assert.DoesNotContain(report.Warnings, diagnostic => diagnostic.Code == "ControllerDuringNoteIgnored");
        }

        /// <summary>pedal で延びている期間内の CC11 も次の On から適用して診断する。</summary>
        [Fact]
        public void ExpressionChangeDuringSustainIsDiagnosed()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 B0 40 7F 00 90 3C 7F 01 80 3C 00 01 B0 0B 40 00 90 40 7F 01 80 40 00 01 B0 40 00 00 FF 2F 00"));
            Assert.Equal(new[] { 12, 6 }, notes.Select(note => note.Volume));
            Assert.All(notes, note => Assert.Equal(4L, note.EndTick));
            Assert.Equal("ControllerDuringNoteIgnored", Assert.Single(report.Warnings).Code);
        }

        /// <summary>pan・bank・bend・圧力・modulation・非対応 CC は規定の値だけ警告する。</summary>
        [Theory]
        [InlineData("B0 0A 40", null)]
        [InlineData("B0 0A 3F", "MidiPanIgnored")]
        [InlineData("B0 00 00", null)]
        [InlineData("B0 00 01", "MidiBankIgnored")]
        [InlineData("B0 20 00", null)]
        [InlineData("B0 20 01", "MidiBankIgnored")]
        [InlineData("E0 00 40", null)]
        [InlineData("E0 01 40", "MidiExpressionIgnored")]
        [InlineData("E0 7F 3F", "MidiExpressionIgnored")]
        [InlineData("A0 3C 00", null)]
        [InlineData("A0 3C 01", "MidiExpressionIgnored")]
        [InlineData("D0 00", null)]
        [InlineData("D0 01", "MidiExpressionIgnored")]
        [InlineData("B0 01 00", null)]
        [InlineData("B0 01 01", "MidiExpressionIgnored")]
        [InlineData("B0 65 00", "MidiExpressionIgnored")]
        [InlineData("B0 64 7F", "MidiExpressionIgnored")]
        [InlineData("B0 63 00", "MidiExpressionIgnored")]
        [InlineData("B0 62 00", "MidiExpressionIgnored")]
        [InlineData("B0 06 00", "MidiExpressionIgnored")]
        [InlineData("B0 42 00", "MidiExpressionIgnored")]
        public void UnsupportedExpressionUsesSpecifiedWarningConditions(string message, string? warningCode)
        {
            ConversionReport report = MidiFileFixture.Report(strict: true);
            Assert.Empty(MidiNoteCollectorTests.Collect(report, MidiFileFixture.Bytes($"02 {message} 00 FF 2F 00")));
            if (warningCode is null)
            {
                Assert.Empty(report.Warnings);
                Assert.True(report.CanWrite);
                return;
            }
            ConversionDiagnostic warning = Assert.Single(report.Warnings);
            Assert.Equal(warningCode, warning.Code);
            Assert.Equal(1, warning.SourceChannel);
            Assert.Equal(2L, warning.SourceTick);
            Assert.Equal(0L, warning.SourceEvent);
            Assert.False(report.CanWrite);
        }

        /// <summary>因子の組み合わせが違っても同じ音量積なら、割り当て用実効音量も完全一致する。</summary>
        [Fact]
        public void EqualVolumeProductsRemainExactPriorityTies()
        {
            ConversionReport report = MidiFileFixture.Report();
            IReadOnlyList<MidiNote> notes = MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 B0 07 40 00 90 3C 7F 01 80 3C 00 00 B0 07 7F 00 90 40 40 01 80 40 00 00 FF 2F 00"));
            Assert.Equal(notes[0].EffectiveVolume, notes[1].EffectiveVolume);
            Assert.Equal(notes[0].Volume, notes[1].Volume);
            Assert.Empty(report.Warnings);
        }

        /// <summary>CC は別チャンネルの発音を警告の根拠にしない。</summary>
        [Fact]
        public void VolumeChangeDoesNotAffectAnotherChannel()
        {
            ConversionReport report = MidiFileFixture.Report();
            MidiNote note = Assert.Single(MidiNoteCollectorTests.Collect(report,
                MidiFileFixture.Bytes("00 91 3C 7F 01 B0 07 40 01 81 3C 00 00 FF 2F 00")));
            Assert.Equal(12, note.Volume);
            Assert.Empty(report.Warnings);
        }
    }
}
