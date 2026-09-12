using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Core.Tests.Formats.Export.Nsf;
using Arpeggio.Core.Tests.Formats.Export.Vgm;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.Nsf;

namespace Arpeggio.Core.Tests.Formats.Export.Nes
{
    /// <summary>Song から保存した VGM と NSF を、独立レジスタ再合成で相互検証する。</summary>
    public sealed class NesRegisterResynthesisTests
    {
        private const int ConcertNote = 69;
        private const int SongTicks = 96;
        private const int OnTick = 12;
        private const int NoteTicks = 48;
        private const int SamplesPerTick = 441;
        private const int ObservationSamples = 11025;
        private const int TailSamples = 4410;

        /// <summary>生成列を復号した全 duty は、一周期の high ステップ数まで正確である。</summary>
        [Theory]
        [InlineData(DutyCycle.Percent12_5, 1)]
        [InlineData(DutyCycle.Percent25, 2)]
        [InlineData(DutyCycle.Percent50, 4)]
        [InlineData(DutyCycle.Percent75, 6)]
        public void GeneratedDutyHasExactCycleRatio(DutyCycle duty, int expectedHighSteps)
        {
            const int pulseStepCycles = 508;
            Song song = CreateSong();
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).Duty = duty;
            AddNote(song, 0, 0, NoteTicks, ConcertNote);
            var (timeline, report) = VgmWriterTests.Compile(song);
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
            var chip = new NesRegisterTraceChip();
            foreach (var write in parsed.Writes.Where(write => write.Sample == 0))
            {
                chip.Apply(write.Address, write.Value);
            }
            int highSteps = 0;
            chip.AdvanceCycles(1);
            for (int step = 0; step < 8; step++)
            {
                if (chip.Level(0) > 0)
                {
                    highSteps++;
                }
                chip.AdvanceCycles(pulseStepCycles);
            }
            Assert.Equal(expectedHighSteps, highSteps);
        }

        /// <summary>生成列の個別 Off は他三声を保ち、曲終端では全 gate を落とす。</summary>
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void GeneratedIndividualStopPreservesOtherGates(int stoppedTrack)
        {
            Song song = CreateSong();
            for (int track = 0; track < 4; track++)
            {
                AddNote(song, track, 0, track == stoppedTrack ? OnTick : NoteTicks, ConcertNote);
            }
            var (timeline, report) = VgmWriterTests.Compile(song);
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
            var chip = new NesRegisterTraceChip();
            foreach (var group in parsed.Writes.GroupBy(write => write.Sample))
            {
                foreach (var write in group)
                {
                    chip.Apply(write.Address, write.Value);
                }
                if (group.Key == OnTick * SamplesPerTick)
                {
                    for (int track = 0; track < 4; track++)
                    {
                        Assert.Equal(track != stoppedTrack, chip.IsGated(track));
                    }
                }
            }
            for (int track = 0; track < 4; track++)
            {
                Assert.False(chip.IsGated(track));
            }
        }

        /// <summary>両 Pulse／Triangle の A4・オクターブ下は周期誤差 2% 以内で、先頭と終端は交流成分がない。</summary>
        [Theory]
        [InlineData(0, 69, 253, 16)] [InlineData(1, 69, 253, 16)] [InlineData(2, 69, 126, 32)]
        [InlineData(0, 57, 507, 16)] [InlineData(1, 57, 507, 16)] [InlineData(2, 57, 253, 32)]
        public void GeneratedToneHasTheoreticalFrequencyAndStops(int track, int note, int timer, int divider)
        {
            Song song = CreateSong();
            AddNote(song, track, OnTick, NoteTicks, note);
            var (timeline, report) = VgmWriterTests.Compile(song);
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
            var chip = new NesRegisterTraceChip();
            var audio = RegisterTraceRenderer.Render(chip, parsed.Writes, checked((int)parsed.WaitSamples) + TailSamples);
            Assert.Equal(timer, chip.Timer(track));
            Assert.Equal(0.0, RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan(0, OnTick * SamplesPerTick)));
            ReadOnlySpan<double> sounding = audio.Left.AsSpan(OnTick * SamplesPerTick + 100, ObservationSamples);
            RegisterTraceAssertions.HasFrequency(sounding, 1789773.0 / (divider * (timer + 1)));
            Assert.True(RegisterTraceAssertions.AlternatingRootMeanSquare(sounding) > 0);
            Assert.Equal(0.0, RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan((OnTick + NoteTicks) * SamplesPerTick)));
            Assert.Equal(audio.Left, audio.Right);
            Assert.False(chip.IsGated(track));
        }

        /// <summary>生成された一定音量に対して、同じ再合成条件の交流振幅が単調に増える。</summary>
        [Theory]
        [InlineData(0)] [InlineData(3)]
        public void GeneratedAmplitudeIsMonotonic(int track)
        {
            double previous = 0;
            foreach (int volume in new[] { 1, 5, 10, 15 })
            {
                Song song = CreateSong();
                AddNote(song, track, 0, NoteTicks, track == 3 ? 10 : ConcertNote).Volume = volume;
                var (timeline, report) = VgmWriterTests.Compile(song);
                ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
                var audio = RegisterTraceRenderer.Render(new NesRegisterTraceChip(), parsed.Writes, (int)parsed.WaitSamples + 1);
                double amplitude = RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan(100, ObservationSamples));
                Assert.True(amplitude > previous);
                previous = amplitude;
            }
        }

        /// <summary>両 mode の生成 Noise は非無音で、周波数推定を使わず周期設定と全停止を検証する。</summary>
        [Theory]
        [InlineData(NoiseMode.Long)]
        [InlineData(NoiseMode.Short)]
        public void GeneratedNoiseIsAudibleAndStops(NoiseMode mode)
        {
            Song song = CreateSong();
            Assert.IsType<NesNoiseInstrument>(song.Instruments[2]).NoiseMode = mode;
            AddNote(song, 3, OnTick, NoteTicks, 10);
            var (timeline, report) = VgmWriterTests.Compile(song);
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(timeline, song.Title, report));
            var chip = new NesRegisterTraceChip();
            var audio = RegisterTraceRenderer.Render(chip, parsed.Writes, (int)parsed.WaitSamples + TailSamples);
            Assert.Equal(380, chip.NoisePeriod);
            Assert.True(RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan(OnTick * SamplesPerTick, ObservationSamples)) > 0);
            Assert.Equal(0.0, RegisterTraceAssertions.AlternatingRootMeanSquare(audio.Left.AsSpan((OnTick + NoteTicks) * SamplesPerTick)));
            Assert.False(chip.IsGated(3));
        }

        /// <summary>四声・同値再発音・個別 Off・有限二周の NSF PLAY 列と量子化 VGM 列が全時刻・順序・再合成出力まで一致する。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void NsfAndQuantizedVgmHaveIdenticalPerformance(int loops)
        {
            Song song = CreateSong();
            song.LoopStartTick = OnTick;
            Assert.IsType<NesPulseInstrument>(song.Instruments[0]).DutyMacro = new Macro { Values = new[] { 1, 2, 3, 4 }, LoopIndex = 0 };
            for (int track = 0; track < 4; track++)
            {
                AddNote(song, track, OnTick, 12, ConcertNote);
                AddNote(song, track, OnTick + 12, 24 + track * 4, ConcertNote);
            }
            ControlTimelineResult source = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Nsf, Loops = loops });
            Assert.NotNull(source.Timeline);
            NsfFrameTimeline? frames = NsfFrameCompiler.Compile(source.Timeline, source.Report);
            Assert.NotNull(frames);
            ParsedVgm parsed = IndependentVgmParser.Parse(QuantizedNesVgmFixture.Write(frames));
            var (_, file, report) = NsfWriterFixture.Save(frames);
            var processor = new Limited6502(file.Memory);
            processor.Call(file.InitAddress, 20000);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, frames, report.Statistics["maximumPlayCycles"]);
            var observed = file.Memory.Writes.Where(write => write.Address is >= 0x4000 and <= 0x4017)
                .Select((write, order) => (Sample: QuantizedNesVgmFixture.SampleAtFrame(write.Frame),
                    Address: (int)write.Address, Value: (int)write.Value, Order: order)).ToArray();
            Assert.Equal(observed, parsed.Writes);
            Assert.Equal(QuantizedNesVgmFixture.SampleAtFrame(frames.EndFrame), parsed.WaitSamples);
            Assert.Equal(734, QuantizedNesVgmFixture.SampleAtFrame(1));
            Assert.Equal(73378, QuantizedNesVgmFixture.SampleAtFrame(100));
            Assert.Contains(parsed.Writes, write => write.Sample > 0 && write.Address == 0x4003);
            Assert.All(observed.Where(write => write.Address == 0x4015), write => Assert.Equal(0, write.Value & 0x10));
            int sampleCount = (int)parsed.WaitSamples + TailSamples;
            var fromNsf = RegisterTraceRenderer.Render(new NesRegisterTraceChip(), observed, sampleCount);
            var fromVgm = RegisterTraceRenderer.Render(new NesRegisterTraceChip(), parsed.Writes, sampleCount);
            Assert.Equal(fromNsf.Left, fromVgm.Left);
            Assert.Equal(fromNsf.Right, fromVgm.Right);
            Assert.True(RegisterTraceAssertions.AlternatingRootMeanSquare(fromNsf.Left) > 0);
            Assert.Equal(0.0, RegisterTraceAssertions.AlternatingRootMeanSquare(fromNsf.Left.AsSpan((int)parsed.WaitSamples)));
        }

        private static Song CreateSong()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 125, lengthTicks: SongTicks);
            song.Instruments.Add(new NesTriangleInstrument { Id = 2 });
            song.Instruments.Add(new NesNoiseInstrument { Id = 3 });
            return song;
        }

        private static Note AddNote(Song song, int track, int tick, int duration, int midiNote)
        {
            var note = new Note { Tick = tick, DurationTicks = duration, MidiNote = midiNote, InstrumentId = track < 2 ? 1 : track };
            song.Tracks[track].Notes.Add(note);
            return note;
        }
    }
}
