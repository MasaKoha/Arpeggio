using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>GM 全 program・チップ既定値・採用順の音色共有を検証する。</summary>
    public sealed class MidiInstrumentMapperTests
    {
        /// <summary>設計表の全 128 program を独立した期待列で検証する。</summary>
        [Fact]
        public void AllProgramsSelectSpecifiedPreset()
        {
            string[] expected = Enumerable.Repeat("piano", 8).Concat(Enumerable.Repeat("bell", 8))
                .Concat(Enumerable.Repeat("organ", 8)).Concat(Enumerable.Repeat("pluck", 8))
                .Concat(Enumerable.Repeat("bass", 8)).Concat(Enumerable.Repeat("strings", 12))
                .Concat(Enumerable.Repeat("choir", 4)).Concat(Enumerable.Repeat("brass", 8))
                .Concat(Enumerable.Repeat("lead", 8)).Concat(Enumerable.Repeat("flute", 8))
                .Concat(Enumerable.Repeat("lead", 8)).Concat(Enumerable.Repeat("strings", 8))
                .Concat(Enumerable.Repeat("bell", 8)).Concat(Enumerable.Repeat("pluck", 8))
                .Concat(Enumerable.Repeat("bell", 8)).Concat(Enumerable.Repeat("lead", 8)).ToArray();
            Assert.Equal(128, expected.Length);
            for (int program = 0; program < expected.Length; program++)
            {
                Assert.Equal(expected[program], MidiInstrumentMapper.GetSnesPreset(program));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiInstrumentMapper.GetSnesPreset(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiInstrumentMapper.GetSnesPreset(128));
        }

        /// <summary>全 program が実際に採用され、SNES は推奨値、NES / GB は同じ Pulse を使う。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void AllProgramsGenerateCompatibleSharedInstruments(ChipKind chip)
        {
            var events = new List<byte>();
            for (int program = 0; program < 128; program++)
            {
                events.AddRange(new byte[] { 0, 0xC0, (byte)program, 0, 0x90, 60, 127, 0x83, 0x60, 0x80, 60, 0 });
            }
            events.AddRange(MidiFileFixture.Bytes("00 FF 2F 00"));
            var result = MidiMappingFixture.Map(MidiFileFixture.Create(events.ToArray()), new MidiImportOptions { Chip = chip });
            Assert.Equal(128, result.Report.WarningCountsByCode["ProgramApproximated"]);
            Assert.Equal(128, result.Tracks.Sum(track => track.Notes.Count));
            if (chip == ChipKind.Snes)
            {
                Assert.Equal(10, result.Instruments.Instruments.Count);
                foreach (SnesSampleInstrument instrument in result.Instruments.Instruments.Cast<SnesSampleInstrument>())
                {
                    SnesInstrumentPreset preset = SnesInstrumentCatalog.Get(instrument.Preset!);
                    Assert.Equal(preset.SampleRate, instrument.SampleRate);
                    Assert.Equal(preset.RootMidiNote, instrument.RootMidiNote);
                    Assert.Equal(preset.AdsrRegisters, instrument.AdsrRegisters);
                    Assert.Equal(preset.Loop, instrument.Loop);
                    Assert.Equal(preset.LoopStart, instrument.LoopStart);
                    Assert.Equal(preset.LoopEnd, instrument.LoopEnd);
                    Assert.Equal(0, instrument.EchoSend);
                    Assert.Equal(0, instrument.Pan);
                    Assert.False(instrument.NoiseEnabled);
                    Assert.False(instrument.PitchModulation);
                    Assert.Null(instrument.SampleData);
                }
            }
            else if (chip == ChipKind.Nes)
            {
                Assert.Equal(DutyCycle.Percent50, Assert.IsType<NesPulseInstrument>(Assert.Single(result.Instruments.Instruments)).Duty);
            }
            else
            {
                GbPulseInstrument instrument = Assert.IsType<GbPulseInstrument>(Assert.Single(result.Instruments.Instruments));
                Assert.Equal(DutyCycle.Percent50, instrument.Duty);
                Assert.Equal(15, instrument.InitialVolume);
                Assert.Equal(0, instrument.EnvelopeStepFrames);
            }
        }

        /// <summary>同時の採用順とチャンネル種別で ID を決定し、未採用 program を生成しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void IdentifiersFollowAcceptanceOrderAndIgnoreUnusedPrograms(ChipKind chip)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 C0 00 00 C1 08 00 C2 10 00 C3 18 00 90 3C 7F 00 91 40 7F 00 92 43 7F 00 93 47 7F 83 60 B0 78 00 00 B1 78 00 00 B2 78 00 00 B3 78 00 00 FF 2F 00"));
            var options = new MidiImportOptions
            {
                Chip = chip,
                ChannelMap = new Dictionary<int, IReadOnlyList<int>>
                {
                    [1] = new[] { 0, 1, 2 }, [2] = new[] { 0, 1, 2 },
                    [3] = new[] { 0, 1, 2 }, [4] = new[] { 0, 1, 2 }
                }
            };
            var first = MidiMappingFixture.Map(bytes, options);
            var second = MidiMappingFixture.Map(bytes, options);
            Assert.Equal(new[] { 71, 67, 64 }, first.Tracks.SelectMany(track => track.Notes).Select(note => note.Pitch));
            Assert.Equal(1, first.Instruments.GetInstrumentId(first.Tracks[0].Notes[0]));
            Assert.Equal(3, first.Report.WarningCountsByCode["ProgramApproximated"]);
            Assert.Equal(first.Instruments.Instruments.Select(instrument => instrument.Name), second.Instruments.Instruments.Select(instrument => instrument.Name));
            Assert.Equal(Enumerable.Range(1, first.Instruments.Instruments.Count), first.Instruments.Instruments.Select(instrument => instrument.Id));
            Assert.NotSame(first.Instruments.Instruments[0], second.Instruments.Instruments[0]);
            if (chip == ChipKind.Snes)
            {
                Assert.Equal(new[] { "pluck", "organ", "bell" }, first.Instruments.Instruments.Select(instrument => instrument.Name));
            }
            else if (chip == ChipKind.GameBoy)
            {
                GbWaveInstrument wave = Assert.IsType<GbWaveInstrument>(first.Instruments.Instruments[1]);
                Assert.Equal(new GbWaveInstrument().Waveform, wave.Waveform);
                Assert.Equal(100, wave.OutputLevel);
            }
            else
            {
                Assert.IsType<NesTriangleInstrument>(first.Instruments.Instruments[1]);
            }
        }

        /// <summary>同じ family は共有し、同じ program でも異なる channel はそれぞれ警告する。</summary>
        [Fact]
        public void SharedPresetReportsEachUsedChannelProgramOnce()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 C0 01 00 C1 02 00 90 3C 7F 00 91 40 7F 83 60 80 3C 00 00 81 40 00 00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00"));
            var result = MidiMappingFixture.Map(bytes, new MidiImportOptions { Chip = ChipKind.Snes });
            Assert.Single(result.Instruments.Instruments);
            Assert.Equal(2, result.Report.WarningCountsByCode["ProgramApproximated"]);
            Assert.All(result.Tracks.SelectMany(track => track.Notes), note => Assert.Equal(1, result.Instruments.GetInstrumentId(note)));
        }

        /// <summary>低い出力トラックを走査した順ではなく、時系列の採用順に採番する。</summary>
        [Fact]
        public void EarlierAcceptedInstrumentPrecedesLaterLowerTrack()
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 C0 00 00 C1 08 00 90 3C 7F 83 60 91 40 7F 83 60 80 3C 00 00 81 40 00 00 FF 2F 00"));
            var options = new MidiImportOptions
            {
                Chip = ChipKind.Snes,
                ChannelMap = new Dictionary<int, IReadOnlyList<int>> { [1] = new[] { 7 }, [2] = new[] { 0 } }
            };
            var result = MidiMappingFixture.Map(bytes, options);
            Assert.Equal(new[] { "piano", "bell" }, result.Instruments.Instruments.Select(instrument => instrument.Name));
            Assert.Equal(1, result.Instruments.GetInstrumentId(Assert.Single(result.Tracks[7].Notes)));
            Assert.Equal(2, result.Instruments.GetInstrumentId(Assert.Single(result.Tracks[0].Notes)));
        }
    }
}
