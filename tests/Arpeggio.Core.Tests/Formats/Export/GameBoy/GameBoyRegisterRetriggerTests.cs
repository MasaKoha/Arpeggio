using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Xunit;
using Arpeggio.Core.Tests.Formats.Export.Vgm;

namespace Arpeggio.Core.Tests.Formats.Export.GameBoy
{
    /// <summary>生成された継続音更新を独立モデルで再構成し、再 trigger と他声の保持を検証する。</summary>
    public sealed class GameBoyRegisterRetriggerTests
    {
        private const int FrameSamples = 735;
        private const int FrameTicks = 2;
        private const int ConcertNote = 69;
        private const int NoiseTrack = 3;
        private const int WaveTrack = 2;

        /// <summary>音量ゼロの保持と復帰で Pulse／Noise だけを再 trigger し、Wave と他 Pulse を維持する。</summary>
        [Fact]
        public void ZeroVolumeRecoveryRetriggersOnlyAffectedChannels()
        {
            Song song = GameBoyRegisterResynthesisTests.CreateSong();
            song.TempoBpm = 150;
            song.LengthTicks = 12;
            var volumeMacro = new Macro { Values = new[] { 15, 0, 0, 8 } };
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).VolumeMacro = volumeMacro;
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).VolumeMacro = volumeMacro;
            song.Instruments.Add(new GbPulseInstrument { Id = 4 });
            for (int track = 0; track < 4; track++)
            {
                Note note = GameBoyRegisterResynthesisTests.AddNote(song, track, 0, song.LengthTicks, track == NoiseTrack ? 96 : ConcertNote);
                if (track == 1)
                {
                    note.InstrumentId = 4;
                }
            }
            ParsedVgm parsed = GameBoyRegisterResynthesisTests.CompileAndParse(song);
            var chip = new GameBoyRegisterTraceChip();
            int writeIndex = 0;
            long elapsedCycles = 0;
            for (int frame = 0; frame <= song.LengthTicks / FrameTicks; frame++)
            {
                long sample = (long)frame * FrameSamples;
                long cycles = sample * chip.ClockRate / RegisterTraceRenderer.SampleRate;
                chip.AdvanceCycles((int)(cycles - elapsedCycles));
                elapsedCycles = cycles;
                while (writeIndex < parsed.Writes.Count && parsed.Writes[writeIndex].Sample <= sample)
                {
                    var write = parsed.Writes[writeIndex++];
                    if (write.Address is >= 0xFF30 and <= 0xFF3F)
                    {
                        Assert.False(chip.DacEnabled(WaveTrack));
                    }
                    chip.Apply(write.Address, write.Value);
                }
                AssertFrame(chip, frame);
            }
            Assert.Equal(16, parsed.Writes.Count(write => write.Address is >= 0xFF30 and <= 0xFF3F));
            Assert.Equal(parsed.Writes.Count, writeIndex);
        }

        /// <summary>60 Hz envelope の正音量変化は毎回 trigger され、同時の新周期も先に設定される。</summary>
        [Fact]
        public void SoftwareEnvelopeUsesNewPitchBeforeEveryRetrigger()
        {
            Song song = GameBoyRegisterResynthesisTests.CreateSong();
            song.TempoBpm = 150;
            song.LengthTicks = 8;
            GbPulseInstrument instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = 3;
            instrument.EnvelopeIncreasing = true;
            instrument.EnvelopeStepFrames = 1;
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 12 } };
            GameBoyRegisterResynthesisTests.AddNote(song, 0, 0, song.LengthTicks, ConcertNote);
            ParsedVgm parsed = GameBoyRegisterResynthesisTests.CompileAndParse(song);
            var chip = new GameBoyRegisterTraceChip();
            int triggerCount = 0;
            foreach (var write in parsed.Writes)
            {
                chip.Apply(write.Address, write.Value);
                if (write.Address != 0xFF14 || (write.Value & 0x80) == 0)
                {
                    continue;
                }
                Assert.Equal(write.Sample == 0 ? 1750 : 1899, chip.Frequency(0));
                Assert.True(chip.DacEnabled(0));
                Assert.True(chip.IsActive(0));
                Assert.Equal(0, chip.Routing);
                Assert.Equal(triggerCount + 3, Assert.Single(parsed.Writes,
                    candidate => candidate.Sample == write.Sample && candidate.Address == 0xFF12 && candidate.Value > 0).Value >> 4);
                triggerCount++;
            }
            Assert.Equal(4, triggerCount);
            Assert.Equal(4, chip.Triggers(0));
        }

        /// <summary>Wave の各 On は DAC off で全 RAM を再ロードし、有限二周でも trigger と周期を維持する。</summary>
        [Fact]
        public void WaveReloadsRamOnEveryOnsetIncludingLoopRestart()
        {
            Song song = GameBoyRegisterResynthesisTests.CreateSong();
            song.TempoBpm = 150;
            song.LengthTicks = 12;
            song.LoopStartTick = 4;
            GameBoyRegisterResynthesisTests.AddNote(song, WaveTrack, 2, 2, ConcertNote);
            GameBoyRegisterResynthesisTests.AddNote(song, WaveTrack, 4, 4, ConcertNote);
            ParsedVgm parsed = GameBoyRegisterResynthesisTests.CompileAndParse(song, loops: 2);
            var chip = new GameBoyRegisterTraceChip();
            int writesSinceTrigger = 0;
            foreach (var write in parsed.Writes)
            {
                if (write.Address is >= 0xFF30 and <= 0xFF3F)
                {
                    Assert.False(chip.DacEnabled(WaveTrack));
                    writesSinceTrigger++;
                }
                chip.Apply(write.Address, write.Value);
                if (write.Address == 0xFF1E && (write.Value & 0x80) != 0)
                {
                    Assert.Equal(16, writesSinceTrigger);
                    Assert.Equal(1899, chip.Frequency(WaveTrack));
                    Assert.Equal(0, chip.Position(WaveTrack));
                    writesSinceTrigger = 0;
                }
            }
            Assert.Equal(3, chip.Triggers(WaveTrack));
            Assert.Equal(0, writesSinceTrigger);
            Assert.Equal(0, chip.Routing);
            Assert.False(chip.DacEnabled(WaveTrack));
        }

        private static void AssertFrame(GameBoyRegisterTraceChip chip, int frame)
        {
            if (frame == 6)
            {
                Assert.Equal(0, chip.Routing);
                Assert.Equal((0.0, 0.0), chip.Output);
                return;
            }
            bool muted = frame is 1 or 2;
            Assert.Equal(muted ? 0x66 : 0xFF, chip.Routing);
            Assert.Equal(!muted, chip.IsActive(0));
            Assert.Equal(!muted, chip.IsActive(NoiseTrack));
            Assert.True(chip.IsActive(1));
            Assert.True(chip.IsActive(WaveTrack));
            Assert.Equal(1, chip.Triggers(1));
            Assert.Equal(1, chip.Triggers(WaveTrack));
            Assert.Equal(frame < 3 ? 1 : 2, chip.Triggers(0));
            Assert.Equal(frame < 3 ? 1 : 2, chip.Triggers(NoiseTrack));
            if (frame == 3)
            {
                Assert.Equal(0, chip.NoiseState);
                Assert.Equal(1750, chip.Frequency(0));
            }
        }
    }
}
