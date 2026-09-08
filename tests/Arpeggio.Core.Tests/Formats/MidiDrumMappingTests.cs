using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats.Midi;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>設計表の打撃・固定 gate・Noise マクロと既存割り当ての接続を検証する。</summary>
    public sealed class MidiDrumMappingTests
    {
        /// <summary>表の全番号と未知番号の各チップ設定を固定値で検証する。</summary>
        [Theory]
        [InlineData("35,36", "kick", 15, 72, 29, 18)]
        [InlineData("37,38,39,40", "snare", 10, 96, 14, 9)]
        [InlineData("42,44", "hat", 1, 120, 4, 3)]
        [InlineData("46", "openhat", 2, 112, 19, 12)]
        [InlineData("41,43,45,47,48,50", "tom", 12, 88, 19, 12)]
        [InlineData("49,51,52,53,55,57,59", "crash", 3, 104, 77, 48)]
        [InlineData("0,34,54,56,58,60,127", "snare", 10, 96, 14, 9)]
        public void DrumTableProducesSpecifiedGateAndInstrument(string pitches, string preset,
            int nesSelection, int gameBoySelection, int endTick, int macroFrames)
        {
            foreach (string pitchText in pitches.Split(','))
            {
                int pitch = int.Parse(pitchText, System.Globalization.CultureInfo.InvariantCulture);
                foreach (ChipKind chip in new[] { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes })
                {
                    VerifyDrum(pitch, chip, preset, nesSelection, gameBoySelection, endTick, macroFrames);
                }
            }
        }

        /// <summary>同時打撃は音量より分類が優先し、SNES の交差 map でもドラムが先になる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void SimultaneousDrumsUseCategoryPriority(ChipKind chip)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes(
                "00 99 2A 7F 00 99 2E 7F 00 99 31 7F 00 99 29 7F 00 99 26 7F 00 99 24 01 00 90 3C 7F 83 60 80 3C 00 00 FF 2F 00"));
            var result = MidiMappingFixture.Map(bytes, new MidiImportOptions
            {
                Chip = chip,
                ChannelMap = chip == ChipKind.Snes ? new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyList<int>>
                {
                    [1] = new[] { 0, 1, 2, 3, 4, 5 }, [10] = new[] { 0, 1, 2, 3, 4, 5 }
                } : null
            });
            if (chip == ChipKind.Snes)
            {
                Assert.Equal(new[] { "kick", "snare", "tom", "crash", "openhat", "hat" },
                    result.Instruments.Instruments.Select(instrument => instrument.Name));
                Assert.Equal(1, result.Report.Statistics["polyphonyNotesDropped"]);
            }
            else
            {
                MidiAllocatedNote drum = Assert.Single(result.Tracks[3].Notes);
                Assert.Equal(36, drum.Source.Timing.Source.Pitch);
                Assert.Equal(1, drum.Volume);
                Assert.Equal(5, result.Report.Statistics["polyphonyNotesDropped"]);
            }
        }

        /// <summary>実ドラムの存在に応じて SNES の 6＋2 声と旋律 8 声を使い分ける。</summary>
        [Theory]
        [InlineData(false, 8)]
        [InlineData(true, 6)]
        public void ActualDrumsReserveTwoSnesVoices(bool hasDrums, int melodicNotes)
        {
            var events = new System.Collections.Generic.List<byte>();
            for (int pitch = 60; pitch < 68; pitch++)
            {
                events.AddRange(new byte[] { 0, 0x90, (byte)pitch, 127 });
            }
            if (hasDrums)
            {
                events.AddRange(MidiFileFixture.Bytes("00 99 24 7F 00 99 2A 7F"));
            }
            events.AddRange(MidiFileFixture.Bytes("83 60 FF 2F 00"));
            var result = MidiMappingFixture.Map(MidiFileFixture.Create(events.ToArray()), new MidiImportOptions { Chip = ChipKind.Snes });
            Assert.Equal(melodicNotes, result.Tracks.SelectMany(track => track.Notes).Count(note => !note.Source.Timing.Source.IsDrum));
            Assert.Equal(hasDrums, Assert.Single(result.Tracks[6].Notes).Source.Timing.Source.IsDrum);
            Assert.Equal(hasDrums, Assert.Single(result.Tracks[7].Notes).Source.Timing.Source.IsDrum);
        }

        /// <summary>後発打撃は選択した polyphony に従い、元 Off は固定 gate を短縮しない。</summary>
        [Theory]
        [InlineData(MidiPolyphonyMode.StealOldest, 2, 10)]
        [InlineData(MidiPolyphonyMode.DropNew, 1, 29)]
        public void LaterDrumUsesPolyphonyPolicy(MidiPolyphonyMode mode, int count, int firstEnd)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes("00 99 24 7F 01 89 24 00 63 99 2A 7F 01 89 2A 00 00 FF 2F 00"));
            var result = MidiMappingFixture.Map(bytes, new MidiImportOptions { Chip = ChipKind.Nes, Polyphony = mode });
            Assert.Equal(count, result.Tracks[3].Notes.Count);
            Assert.Equal(firstEnd, result.Tracks[3].Notes[0].EndTick);
        }

        /// <summary>CC120 は元 Off 後も停止させ、CC123 と sustain は固定 gate を変えない。</summary>
        [Theory]
        [InlineData("0A B9 78 00", 2)]
        [InlineData("0A B9 7B 00", 29)]
        [InlineData("0A B9 40 7F", 29)]
        public void DrumControllersRespectFixedGate(string controller, int expectedEnd)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes($"00 99 24 7F 0A 89 24 00 {controller} 00 FF 2F 00"));
            var result = MidiMappingFixture.Map(bytes, new MidiImportOptions { Chip = ChipKind.Nes });
            Assert.Equal(expectedEnd, Assert.Single(result.Tracks[3].Notes).EndTick);
            Assert.DoesNotContain(result.Report.Warnings, warning => warning.Code == "UnclosedNote" || warning.Code == "UnmatchedNoteOff");
        }

        /// <summary>量子化前の元 gate が一致すれば置換警告を出さず、欠落した Off は警告する。</summary>
        [Theory]
        [InlineData("82 20 89 24 00", false)]
        [InlineData("", true)]
        public void OriginalGateComparisonUsesExactTime(string ending, bool replaced)
        {
            byte[] bytes = MidiFileFixture.Create(MidiFileFixture.Bytes($"00 99 24 7F {ending} 00 FF 2F 00"));
            var result = MidiMappingFixture.Map(bytes, new MidiImportOptions { Chip = ChipKind.Snes });
            Assert.Equal(replaced, result.Report.Warnings.Any(warning => warning.Code == "DrumGateReplaced"));
        }

        /// <summary>元 Off 後・同 tick の On 後だけを含め、実際に値が変わった CC を診断する。</summary>
        [Theory]
        [InlineData("00 B9 07 64 00 99 24 7F 01 89 24 00 01 B9 07 50", 1)]
        [InlineData("00 99 24 7F 00 B9 07 50", 1)]
        [InlineData("00 B9 07 50 00 99 24 7F", 0)]
        [InlineData("00 99 24 7F 00 B9 07 64", 0)]
        [InlineData("00 99 24 7F 82 20 B9 0B 50", 0)]
        [InlineData("00 99 24 7F 01 B9 78 00 01 B9 07 50", 0)]
        [InlineData("00 B9 07 50 00 99 24 7F 01 B9 79 00", 1)]
        public void VolumeChangesAreDiagnosedWithinRealDrumGate(string events, int expected)
        {
            var result = MidiMappingFixture.Map(MidiFileFixture.Create(MidiFileFixture.Bytes(events + " 00 FF 2F 00")),
                new MidiImportOptions { Chip = ChipKind.Nes });
            Assert.Equal(expected, result.Report.Warnings.Count(warning => warning.Code == "ControllerDuringNoteIgnored"));
        }

        private static void VerifyDrum(int pitch, ChipKind chip, string preset, int nesSelection,
            int gameBoySelection, int endTick, int macroFrames)
        {
            byte[] bytes = MidiFileFixture.Create(new byte[] { 0, 0x99, (byte)pitch, 127, 1, 0x89, (byte)pitch, 0, 0, 0xFF, 0x2F, 0 });
            var result = MidiMappingFixture.Map(bytes, new MidiImportOptions { Chip = chip });
            MidiAllocatedNote note = Assert.Single(result.Tracks.SelectMany(track => track.Notes));
            Instrument instrument = Assert.Single(result.Instruments.Instruments);
            Assert.Equal(preset, instrument.Name);
            int[] known = { 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 55, 57, 59 };
            Assert.Equal(!known.Contains(pitch), result.Report.Warnings.Any(warning => warning.Code == "UnknownDrumMapped"));
            Assert.Equal(endTick, note.EndTick);
            Assert.Equal(12, note.Volume);
            Assert.Equal(1, result.Instruments.GetInstrumentId(note));
            Assert.Single(result.Report.Warnings, warning => warning.Code == "DrumGateReplaced");
            Assert.DoesNotContain(result.Report.Warnings, warning => warning.Code == "MidiPitchClamped");
            if (chip == ChipKind.Snes)
            {
                SnesSampleInstrument sample = Assert.IsType<SnesSampleInstrument>(instrument);
                Assert.Equal(preset, sample.Preset);
                Assert.Equal(60, sample.RootMidiNote);
                Assert.Equal(60, note.Pitch);
                Assert.Equal(0, sample.EchoSend);
                Assert.False(sample.NoiseEnabled);
                Assert.DoesNotContain(result.Report.Warnings, warning => warning.Code == "DrumApproximated");
                return;
            }
            Macro macro;
            if (chip == ChipKind.Nes)
            {
                NesNoiseInstrument noise = Assert.IsType<NesNoiseInstrument>(instrument);
                Assert.Equal(NoiseMode.Long, noise.NoiseMode);
                Assert.Equal(nesSelection, note.Pitch);
                macro = noise.VolumeMacro!;
            }
            else
            {
                GbNoiseInstrument noise = Assert.IsType<GbNoiseInstrument>(instrument);
                Assert.Equal(15, noise.LfsrWidth);
                Assert.Equal(gameBoySelection, note.Pitch);
                macro = noise.VolumeMacro!;
            }
            Assert.Single(result.Report.Warnings, warning => warning.Code == "DrumApproximated");
            Assert.NotNull(macro);
            Assert.Equal(-1, macro.LoopIndex);
            Assert.Equal(macroFrames + 1, macro.Values.Length);
            Assert.Equal(15, macro.Values[0]);
            Assert.Equal(0, macro.Values[macroFrames]);
            for (int frame = 0; frame <= macroFrames; frame++)
            {
                int expected = (int)decimal.Round(15m * (macroFrames - frame) / macroFrames, MidpointRounding.AwayFromZero);
                Assert.Equal(expected, macro.Values[frame]);
            }
        }
    }
}
