using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>チップの整数音域と、サンプル元レートを含む SNES の端点を固定値で検証する。</summary>
    public sealed class MidiPitchRangeTests
    {
        /// <summary>連続音域の内側へ ceil / floor した NES / GB の端点を使う。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ChannelKind.Pulse, 33, 126)]
        [InlineData(ChipKind.Nes, ChannelKind.Triangle, 21, 114)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Pulse, 36, 127)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Wave, 24, 127)]
        public void ChipRangesUseInnerIntegerEndpoints(ChipKind chip, ChannelKind channel, int minimum, int maximum)
        {
            MidiPitchRange range = MidiPitchRange.ForChip(chip, channel);
            Assert.Equal(minimum, range.Minimum);
            Assert.Equal(maximum, range.Maximum);
        }

        /// <summary>各出力声で上下端を最寄りクランプし、整数範囲内は変更しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 0, 33, 126)]
        [InlineData(ChipKind.Nes, 1, 33, 126)]
        [InlineData(ChipKind.Nes, 2, 21, 114)]
        [InlineData(ChipKind.GameBoy, 0, 36, 127)]
        [InlineData(ChipKind.GameBoy, 1, 36, 127)]
        [InlineData(ChipKind.GameBoy, 2, 24, 127)]
        public void AllocationClampsAfterSelectingOutputVoice(ChipKind chip, int outputTrack, int minimum, int maximum)
        {
            var notes = new[]
            {
                MidiVoiceFixture.Note(0, startTick: 0, endTick: 10),
                MidiVoiceFixture.Note(minimum, startTick: 10, endTick: 20),
                MidiVoiceFixture.Note(maximum, startTick: 20, endTick: 30),
                MidiVoiceFixture.Note(127, startTick: 30, endTick: 40)
            };
            var report = new ConversionReport(ConversionFormat.Midi, chip);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(chip, notes, report, channelMap: MidiVoiceFixture.Map(1, outputTrack));
            Assert.Equal(minimum, tracks[outputTrack].Notes[0].Pitch);
            Assert.Equal(minimum, tracks[outputTrack].Notes[1].Pitch);
            Assert.Equal(maximum, tracks[outputTrack].Notes[2].Pitch);
            Assert.Equal(maximum, tracks[outputTrack].Notes[3].Pitch);
            Assert.Equal(maximum == 127 ? 1L : 2L, report.WarningCountsByCode["MidiPitchClamped"]);
            Assert.Contains(report.Warnings, warning => warning.Code == "MidiPitchClamped" && warning.OutputTrack == outputTrack && warning.Original == "0");
        }

        /// <summary>SNES 上端は root だけでなく元 sampleRate に応じて変わる。</summary>
        [Theory]
        [InlineData(32000, 60, 0, 83)]
        [InlineData(16000, 60, 0, 95)]
        [InlineData(24000, 60, 0, 88)]
        [InlineData(48000, 60, 0, 76)]
        [InlineData(1000, 127, 31, 127)]
        public void SnesRangeIncludesRootRateAndRoundedRegister(int sampleRate, int root, int minimum, int maximum)
        {
            MidiPitchRange range = MidiPitchRange.ForSnesSample(sampleRate, root);
            Assert.Equal(minimum, range.Minimum);
            Assert.Equal(maximum, range.Maximum);
            double minimumRegister = Math.Round(Math.Pow(2, (minimum - root) / 12.0) * sampleRate / 32000 * 4096, MidpointRounding.AwayFromZero);
            double maximumRegister = Math.Round(Math.Pow(2, (maximum - root) / 12.0) * sampleRate / 32000 * 4096, MidpointRounding.AwayFromZero);
            Assert.InRange(minimumRegister, 1, 16383);
            Assert.InRange(maximumRegister, 1, 16383);
            if (minimum > 0)
            {
                Assert.Equal(0, Math.Round(Math.Pow(2, (minimum - 1 - root) / 12.0) * sampleRate / 32000 * 4096, MidpointRounding.AwayFromZero));
            }
            if (maximum < 127)
            {
                Assert.True(Math.Round(Math.Pow(2, (maximum + 1 - root) / 12.0) * sampleRate / 32000 * 4096, MidpointRounding.AwayFromZero) > 16383);
            }
        }

        /// <summary>同じ SNES 出力声でもノートごとに選択済みのサンプル音域を適用する。</summary>
        [Fact]
        public void SnesUsesEachNotesSelectedSampleRange()
        {
            var notes = new[]
            {
                MidiVoiceFixture.Note(100, startTick: 0, endTick: 10, sampleRange: MidiPitchRange.ForSnesSample(32000, 60)),
                MidiVoiceFixture.Note(100, startTick: 10, endTick: 20, sampleRange: MidiPitchRange.ForSnesSample(16000, 60)),
                MidiVoiceFixture.Note(0, startTick: 20, endTick: 30, sampleRange: MidiPitchRange.ForSnesSample(1000, 127))
            };
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.Snes);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(ChipKind.Snes, notes, report, channelMap: MidiVoiceFixture.Map(1, 0));
            Assert.Equal(83, tracks[0].Notes[0].Pitch);
            Assert.Equal(95, tracks[0].Notes[1].Pitch);
            Assert.Equal(31, tracks[0].Notes[2].Pitch);
            Assert.Equal(3L, report.WarningCountsByCode["MidiPitchClamped"]);
        }

        /// <summary>Noise selection は音階としてクランプしない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, 0)]
        [InlineData(ChipKind.Nes, 127)]
        [InlineData(ChipKind.GameBoy, 0)]
        [InlineData(ChipKind.GameBoy, 127)]
        public void NoiseSelectionBypassesPitchClamping(ChipKind chip, int selection)
        {
            MidiVoiceNote note = MidiVoiceFixture.Note(36, channel: 10, drumPriority: MidiDrumPriority.Kick, outputPitch: selection);
            var report = new ConversionReport(ConversionFormat.Midi, chip);
            IReadOnlyList<MidiVoiceTrack> tracks = MidiVoiceFixture.Allocate(chip, new[] { note }, report);
            Assert.Equal(selection, Assert.Single(tracks[3].Notes).Pitch);
            Assert.Empty(report.Warnings);
        }

        /// <summary>音域不備は後段の null 参照にせず、割り当て開始前に拒否する。</summary>
        [Fact]
        public void MissingSnesRangeAndInvalidRangesAreRejected()
        {
            var report = new ConversionReport(ConversionFormat.Midi, ChipKind.Snes);
            Assert.Null(MidiVoiceAllocator.Allocate(new[] { MidiVoiceFixture.Note() }, new MidiImportOptions { Chip = ChipKind.Snes }, report));
            Assert.Equal("InvalidMidiVoice", Assert.Single(report.Errors).Code);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MidiPitchRange(-1, 127));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MidiPitchRange(10, 9));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MidiPitchRange(0, 128));
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiPitchRange.ForSnesSample(0, 60));
            Assert.Throws<ArgumentException>(() => MidiPitchRange.ForChip(ChipKind.Nes, ChannelKind.Noise));
        }
    }
}
