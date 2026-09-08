using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>生成した GB VGM を独立パース・再合成して周期・左右・停止・再発音を検証する。</summary>
    public sealed class GameBoyRegisterResynthesisTests
    {
        private const int SongTicks = 96;
        private const int OnTick = 12;
        private const int NoteTicks = 48;
        private const int ConcertNote = 69;
        private const int SamplesPerTick = 441;
        private const int ObservationSamples = 11025;
        private const int TailSamples = 4410;
        private const int WaveTrack = 2;
        private const int NoiseTrack = 3;

        /// <summary>両 Pulse／Wave の A4 と一オクターブ下は理論周期の2%以内で、先頭無音と停止を保つ。</summary>
        [Theory]
        [InlineData(0, 69, 1750, 131072)] [InlineData(1, 69, 1750, 131072)] [InlineData(2, 69, 1899, 65536)]
        [InlineData(0, 57, 1452, 131072)] [InlineData(1, 57, 1452, 131072)] [InlineData(2, 57, 1750, 65536)]
        public void GeneratedTonesHaveTheoreticalFrequencyAndStop(int track, int note, int frequencyRegister, int numerator)
        {
            Song song = CreateSong();
            AddNote(song, track, OnTick, NoteTicks, note);
            ParsedVgm parsed = CompileAndParse(song);
            var chip = new GameBoyRegisterTraceChip();
            var audio = RegisterTraceRenderer.Render(chip, parsed.Writes, (int)parsed.WaitSamples + TailSamples);
            Assert.Equal(frequencyRegister, chip.Frequency(track));
            Assert.All(audio.Left.Take(OnTick * SamplesPerTick), sample => Assert.Equal(0.0, sample));
            ReadOnlySpan<double> sounding = audio.Left.AsSpan(OnTick * SamplesPerTick + 100, ObservationSamples);
            RegisterTraceAssertions.HasFrequency(sounding, (double)numerator / (2048 - frequencyRegister));
            Assert.True(RegisterTraceAssertions.AlternatingRootMeanSquare(sounding) > 0);
            Assert.All(audio.Left.Skip((OnTick + NoteTicks) * SamplesPerTick), sample => Assert.Equal(0.0, sample));
            Assert.Equal(audio.Left, audio.Right);
            Assert.False(chip.IsActive(track));
            Assert.False(chip.DacEnabled(track));
        }

        /// <summary>生成された全 duty の一周期を再構成し、比率を丸め誤差なしで検証する。</summary>
        [Theory]
        [InlineData(DutyCycle.Percent12_5, 1)] [InlineData(DutyCycle.Percent25, 2)]
        [InlineData(DutyCycle.Percent50, 4)] [InlineData(DutyCycle.Percent75, 6)]
        public void GeneratedPulseDutyHasExactCycleRatio(DutyCycle duty, int expectedHighSteps)
        {
            Song song = CreateSong();
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).Duty = duty;
            AddNote(song, 0, 0, NoteTicks, ConcertNote);
            var chip = new GameBoyRegisterTraceChip();
            ApplyInitial(chip, CompileAndParse(song));
            chip.AdvanceCycles(1192 * 8);
            int highSteps = 0;
            for (int step = 0; step < 8; step++)
            {
                if (chip.Level(0) > 0)
                {
                    highSteps++;
                }
                chip.AdvanceCycles(1192);
            }
            Assert.Equal(expectedHighSteps, highSteps);
        }

        /// <summary>四声それぞれの左右と中央の境界を、実際の再合成出力で検証する。</summary>
        [Theory]
        [InlineData(0, -1, true, false)] [InlineData(0, -0.5, true, true)]
        [InlineData(0, 0.5, true, true)] [InlineData(0, 1, false, true)]
        [InlineData(1, -1, true, false)] [InlineData(1, -0.5, true, true)]
        [InlineData(1, 0.5, true, true)] [InlineData(1, 1, false, true)]
        [InlineData(2, -1, true, false)] [InlineData(2, -0.5, true, true)]
        [InlineData(2, 0.5, true, true)] [InlineData(2, 1, false, true)]
        [InlineData(3, -1, true, false)] [InlineData(3, -0.5, true, true)]
        [InlineData(3, 0.5, true, true)] [InlineData(3, 1, false, true)]
        public void GeneratedRoutingProducesExpectedLeftAndRight(int track, double pan, bool leftEnabled, bool rightEnabled)
        {
            Song song = CreateSong();
            song.Tracks[track].Pan = pan;
            AddNote(song, track, OnTick, NoteTicks, track == NoiseTrack ? 96 : ConcertNote);
            ParsedVgm parsed = CompileAndParse(song);
            var audio = RegisterTraceRenderer.Render(new GameBoyRegisterTraceChip(), parsed.Writes, (int)parsed.WaitSamples + TailSamples);
            double left = RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan(OnTick * SamplesPerTick, ObservationSamples));
            double right = RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Right.AsSpan(OnTick * SamplesPerTick, ObservationSamples));
            Assert.Equal(leftEnabled, left > 0);
            Assert.Equal(rightEnabled, right > 0);
            if (leftEnabled && rightEnabled)
            {
                Assert.Equal(audio.Left, audio.Right);
            }
            Assert.All(audio.Left.Skip((OnTick + NoteTicks) * SamplesPerTick), sample => Assert.Equal(0.0, sample));
            Assert.All(audio.Right.Skip((OnTick + NoteTicks) * SamplesPerTick), sample => Assert.Equal(0.0, sample));
        }

        /// <summary>生成された両幅の Noise は正しい周期で非無音となり、終端の DAC off で停止する。</summary>
        [Theory]
        [InlineData(7)] [InlineData(15)]
        public void GeneratedNoiseReconstructsRateAndAudibleOutput(int width)
        {
            Song song = CreateSong();
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).LfsrWidth = width;
            AddNote(song, NoiseTrack, OnTick, NoteTicks, 96);
            ParsedVgm parsed = CompileAndParse(song);
            var chip = new GameBoyRegisterTraceChip();
            var audio = RegisterTraceRenderer.Render(chip, parsed.Writes, (int)parsed.WaitSamples + TailSamples);
            Assert.Equal(128, chip.NoisePeriod);
            Assert.Equal(width == 7 ? 8 : 0, Assert.Single(parsed.Writes, write => write.Address == 0xFF22).Value & 8);
            Assert.True(RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan(OnTick * SamplesPerTick, ObservationSamples)) > 0);
            Assert.All(audio.Left.Skip((OnTick + NoteTicks) * SamplesPerTick), sample => Assert.Equal(0.0, sample));
            Assert.False(chip.DacEnabled(NoiseTrack));
        }

        /// <summary>Pulse／Noise の音量と Wave の段階音量は、同じ観測条件の振幅が単調に増える。</summary>
        [Theory]
        [InlineData(0)] [InlineData(2)] [InlineData(3)]
        public void GeneratedAmplitudeIsMonotonic(int track)
        {
            double previous = -1;
            foreach (int volume in new[] { 0, 4, 8, 15 })
            {
                Song song = CreateSong();
                AddNote(song, track, 0, NoteTicks, track == NoiseTrack ? 96 : ConcertNote).Volume = volume;
                ParsedVgm parsed = CompileAndParse(song);
                var audio = RegisterTraceRenderer.Render(new GameBoyRegisterTraceChip(), parsed.Writes, (int)parsed.WaitSamples + 1);
                double amplitude = RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan(100, ObservationSamples));
                Assert.True(amplitude > previous);
                previous = amplitude;
            }
        }

        /// <summary>生成列の各声 Off は他三声の状態と routing を保ち、終端は四声すべて停止する。</summary>
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void GeneratedIndividualStopPreservesOtherChannels(int stoppedTrack)
        {
            Song song = CreateSong();
            for (int track = 0; track < 4; track++)
            {
                AddNote(song, track, 0, track == stoppedTrack ? OnTick : NoteTicks, ConcertNote);
            }
            ParsedVgm parsed = CompileAndParse(song);
            var chip = new GameBoyRegisterTraceChip();
            foreach (var write in parsed.Writes.Where(write => write.Sample <= OnTick * SamplesPerTick))
            {
                chip.Apply(write.Address, write.Value);
            }
            Assert.Equal(0xFF & ~(0x11 << stoppedTrack), chip.Routing);
            for (int track = 0; track < 4; track++)
            {
                Assert.Equal(track != stoppedTrack, chip.IsActive(track));
                Assert.Equal(track != stoppedTrack, chip.DacEnabled(track));
            }
            foreach (var write in parsed.Writes.Where(write => write.Sample > OnTick * SamplesPerTick))
            {
                chip.Apply(write.Address, write.Value);
            }
            Assert.Equal(0, chip.Routing);
            Assert.Equal((0.0, 0.0), chip.Output);
        }

        internal static Song CreateSong()
        {
            Song song = SongFactory.Create(ChipKind.GameBoy, tempoBpm: 125, lengthTicks: SongTicks);
            song.Instruments.Add(new GbWaveInstrument { Id = 2 });
            song.Instruments.Add(new GbNoiseInstrument { Id = 3 });
            return song;
        }

        internal static Note AddNote(Song song, int track, int tick, int duration, int midiNote)
        {
            var note = new Note { Tick = tick, DurationTicks = duration, MidiNote = midiNote, InstrumentId = track < WaveTrack ? 1 : track };
            song.Tracks[track].Notes.Add(note);
            return note;
        }

        internal static ParsedVgm CompileAndParse(Song song, int loops = 1)
        {
            ControlTimelineResult control = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm, Loops = loops });
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            byte[] bytes = VgmWriterTests.Write(timeline, song.Title, control.Report);
            return IndependentVgmParser.Parse(bytes);
        }

        private static void ApplyInitial(GameBoyRegisterTraceChip chip, ParsedVgm parsed)
        {
            foreach (var write in parsed.Writes.Where(write => write.Sample == 0))
            {
                chip.Apply(write.Address, write.Value);
            }
        }
    }
}
