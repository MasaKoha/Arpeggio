using System;
using System.IO;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>実パーサーを通して入力 byte・イベント・On・曲長の上限を検証する。</summary>
    public sealed class MidiReaderLimitsTests
    {
        /// <summary>format 1 は最大 256 MTrk を許可する。</summary>
        [Fact]
        public void MaximumTrackCountIsAccepted()
        {
            var tracks = new byte[256][];
            Array.Fill(tracks, MidiFileFixture.Bytes("00 FF 2F 00"));
            MidiFile file = MidiFileFixture.Read(MidiFileFixture.Create(tracks), MidiFileFixture.Report());
            Assert.Equal(256, file.TrackCount);
            Assert.Equal(256, file.Events.Count);
        }

        /// <summary>32 MiB ちょうどの入力を許可し、末尾の 1 byte 超過を拒否する。</summary>
        [Fact]
        public void InputByteLimitIncludesSkippedPayload()
        {
            const int MaximumBytes = 33554432;
            const int EmptyFileBytes = 26;
            const int ChunkHeaderBytes = 8;
            using var stream = new MemoryStream();
            stream.Write(MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00")));
            stream.Write(MidiFileFixture.Chunk("4A554E4B", new byte[MaximumBytes - EmptyFileBytes - ChunkHeaderBytes]));
            byte[] bytes = stream.ToArray();
            ConversionReport report = MidiFileFixture.Report();
            MidiFileFixture.Read(bytes, report);
            Assert.Equal(MaximumBytes, report.Statistics["midiInputBytes"]);
            stream.WriteByte(0);
            Assert.Contains(MidiFileFixture.Reject(stream.ToArray()).Errors, diagnostic => diagnostic.Code == "MidiInputLimitExceeded");
        }

        /// <summary>EOT と読み捨て meta を含めて 1000000 イベントまでに制限する。</summary>
        [Theory]
        [InlineData(999999, true)]
        [InlineData(1000000, false)]
        public void EventLimitCountsIgnoredMetadata(int ignoredCount, bool accepted)
        {
            using var track = new MemoryStream();
            byte[] ignored = MidiFileFixture.Bytes("00 FF 01 00");
            for (int eventIndex = 0; eventIndex < ignoredCount; eventIndex++)
            {
                track.Write(ignored);
            }
            track.Write(MidiFileFixture.Bytes("00 FF 2F 00"));
            byte[] bytes = MidiFileFixture.Create(track.ToArray());
            if (!accepted)
            {
                Assert.Contains(MidiFileFixture.Reject(bytes).Errors, diagnostic => diagnostic.Code == "MidiEventLimitExceeded");
                return;
            }
            ConversionReport report = MidiFileFixture.Report();
            MidiFile file = MidiFileFixture.Read(bytes, report);
            Assert.Single(file.Events);
            Assert.Equal(1000000L, report.Statistics["midiEvents"]);
            Assert.Equal(999999L, report.WarningCount);
            Assert.Equal(4096, report.Warnings.Count);
        }

        /// <summary>正の velocity の On は 250000 件まで。velocity 0 は Off として資源数から除く。</summary>
        [Theory]
        [InlineData(250000, true)]
        [InlineData(250001, false)]
        public void NoteOnLimitIsInclusive(int count, bool accepted)
        {
            using var track = new MemoryStream();
            byte[] noteOn = MidiFileFixture.Bytes("00 90 3C 01");
            for (int eventIndex = 0; eventIndex < count; eventIndex++)
            {
                track.Write(noteOn);
            }
            track.Write(MidiFileFixture.Bytes("00 90 3C 00 00 FF 2F 00"));
            byte[] bytes = MidiFileFixture.Create(track.ToArray());
            if (!accepted)
            {
                Assert.Contains(MidiFileFixture.Reject(bytes).Errors, diagnostic => diagnostic.Code == "SourceNoteLimitExceeded");
                return;
            }
            ConversionReport report = MidiFileFixture.Report();
            MidiFileFixture.Read(bytes, report);
            Assert.Equal(250000L, report.Statistics["midiNoteOns"]);
        }

        /// <summary>実時間は整数分子のまま 1800 秒ちょうどと 1 tick 超過を区別する。</summary>
        [Theory]
        [InlineData("E9BC00", true)]
        [InlineData("E9BC01", false)]
        public void DurationLimitIsInclusive(string endDelta, bool accepted)
        {
            // PPQN 480、既定 500000 us で 1728000 tick が 1800 秒。
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes($"{endDelta} FF 2F 00"));
            if (!accepted)
            {
                Assert.Contains(MidiFileFixture.Reject(bytes).Errors, diagnostic => diagnostic.Code == "DurationLimitExceeded");
                return;
            }
            ConversionReport report = MidiFileFixture.Report();
            MidiFileFixture.Read(bytes, report);
            Assert.Equal(1800.0, report.DurationSeconds);
        }

        /// <summary>読み捨てイベントの巨大 delta が実時間分子をあふれさせても、部分列を返さない。</summary>
        [Fact]
        public void DurationNumeratorOverflowIsReported()
        {
            const int LargeDeltaCount = 3000;
            using var track = new MemoryStream();
            track.Write(MidiFileFixture.Bytes("00 FF 51 03 FFFFFF"));
            byte[] ignored = MidiFileFixture.Bytes("FFFFFF7F FF 01 00");
            for (int eventIndex = 0; eventIndex < LargeDeltaCount; eventIndex++)
            {
                track.Write(ignored);
            }
            track.Write(MidiFileFixture.Bytes("00 FF 2F 00"));
            ConversionReport report = MidiFileFixture.Reject(MidiFileFixture.Create(track.ToArray()));
            Assert.Contains(report.Errors, diagnostic => diagnostic.Code == "MidiTimeOverflow");
        }

        /// <summary>別 MTrk のテンポと同 tick の最終テンポも実時間上限へ反映する。</summary>
        [Fact]
        public void DurationUsesMergedTempoOrder()
        {
            byte[] first = MidiFileFixture.Bytes("00 FF 51 03 07A120 8360 FF 51 03 0F4240 8360 FF 2F 00");
            byte[] second = MidiFileFixture.Bytes("00 FF 51 03 03D090 8740 FF 2F 00");
            ConversionReport report = MidiFileFixture.Report();
            MidiFileFixture.Read(MidiFileFixture.Create(first, second), report);
            Assert.Equal(1.25, report.DurationSeconds);
        }
    }
}
