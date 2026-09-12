using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Arpeggio.Core.Synthesis;
using Arpeggio.Core.Synthesis.GameBoy;
using Arpeggio.Formats.Export;
using Xunit;
using static Arpeggio.Core.Tests.Formats.Export.GameBoy.GameBoyRegisterTestData;
using Arpeggio.Core.Tests.Formats.Export.Vgm;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.GameBoy;

namespace Arpeggio.Core.Tests.Formats.Export.GameBoy
{
    /// <summary>GB デューティの PCM・制御列・VGM・保存の接続を検証する。</summary>
    public sealed class GameBoyDutyMacroTests
    {
        private const int SampleRate = 44100;
        private const int FrameCount = 6;
        private const int DutyRegister = 0xFF11;
        private const int DutyRegisterStride = 5;
        private const int TriggerMask = 0x80;
        private const int DutyShift = 6;
        private const int TriggerRegisterOffset = 3;

        /// <summary>全段階と終端保持・ループが位相を継続した PCM と両 Pulse の VGM に一致する。</summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        public void DutyStepsReachAudioControlAndVgmWithoutRetrigger(int loopIndex)
        {
            Song song = CreateSong(FrameTicks * FrameCount);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.DutyMacro = new Macro { Values = new[] { 1, 2, 3, 4 }, LoopIndex = loopIndex };
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            AddNote(song, PulseTwoTrack, 0, song.LengthTicks);
            int[] expectedDuties = loopIndex < 0 ? new[] { 1, 2, 3, 4, 4, 4 } : new[] { 1, 2, 3, 4, 2, 3 };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            foreach (int trackIndex in new[] { PulseOneTrack, PulseTwoTrack })
            {
                ControlEvent[] states = control.Timeline.Events.Where(value => value.TrackIndex == trackIndex && value.Kind != ControlEventKind.NoteOff).ToArray();
                Assert.Equal(expectedDuties, states.Select(value => value.Duty));
                Assert.Equal(Enumerable.Range(0, FrameCount).Select(frame => (long)frame * FrameSamples), states.Select(value => value.PositionSamples));
            }
            float[] actual = RenderFrames(instrument);
            for (int duty = 1; duty <= 4; duty++)
            {
                float[] reference = RenderFrames(new GbPulseInstrument { Duty = (DutyCycle)duty });
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    if (expectedDuties[frame] != duty)
                    {
                        continue;
                    }
                    Assert.Equal(MemoryMarshal.AsBytes(reference.AsSpan(frame * FrameSamples, FrameSamples)).ToArray(),
                        MemoryMarshal.AsBytes(actual.AsSpan(frame * FrameSamples, FrameSamples)).ToArray());
                }
            }
            RegisterTimeline? registers = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(registers);
            ParsedVgm parsed = IndependentVgmParser.Parse(VgmWriterTests.Write(registers, song.Title, control.Report));
            VgmWriterTests.AssertRoundTrip(registers, parsed);
            foreach (int trackIndex in new[] { PulseOneTrack, PulseTwoTrack })
            {
                var writes = parsed.Writes.Where(write => write.Address == DutyRegister + trackIndex * DutyRegisterStride).ToArray();
                int[] expectedFrames = loopIndex < 0 ? new[] { 0, 1, 2, 3 } : new[] { 0, 1, 2, 3, 4, 5 };
                Assert.Equal(expectedFrames.Select(frame => (long)frame * FrameSamples), writes.Select(write => write.Sample));
                Assert.Equal(expectedFrames.Select(frame => (expectedDuties[frame] - 1) << DutyShift), writes.Select(write => write.Value));
                Assert.Single(parsed.Writes, write => write.Address == DutyRegister + trackIndex * DutyRegisterStride + TriggerRegisterOffset && (write.Value & TriggerMask) != 0);
            }
        }

        /// <summary>null と空列は旧位相・音量計算の全 PCM バイトと一致し、ハードウェア包絡を維持する。</summary>
        [Theory]
        [InlineData(DutyCycle.Percent12_5, 0.125)]
        [InlineData(DutyCycle.Percent25, 0.25)]
        [InlineData(DutyCycle.Percent50, 0.5)]
        [InlineData(DutyCycle.Percent75, 0.75)]
        public void MissingAndEmptyMacrosPreserveLegacyPcm(DutyCycle duty, double ratio)
        {
            const int InitialVolume = 9;
            const int EnvelopeInterval = 2;
            var instrument = new GbPulseInstrument { Duty = duty, InitialVolume = InitialVolume, EnvelopeStepFrames = EnvelopeInterval };
            var expected = new float[FrameSamples * FrameCount];
            double phase = 0;
            double increment = PitchTable.Quantize(ChipKind.GameBoy, ChannelKind.Pulse, ConcertNote) / SampleRate;
            for (int index = 0; index < expected.Length; index++)
            {
                int envelope = InitialVolume - index / FrameSamples / EnvelopeInterval;
                expected[index] = (float)((phase < ratio ? 1 : -1) * 1.0 * envelope / FullVolume);
                phase += increment;
                phase -= Math.Floor(phase);
            }
            Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(RenderFrames(instrument).AsSpan()).ToArray());
            instrument.DutyMacro = new Macro();
            Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(RenderFrames(instrument).AsSpan()).ToArray());
        }

        /// <summary>旧 JSON のキーと null は全バイトを保持し、新マクロだけ末尾へ追加して往復する。</summary>
        [Fact]
        public void JsonPreservesLegacyBytesAndClonesDutyMacro()
        {
            const string LegacyJson = "{\n  \"kind\": \"GbPulse\",\n  \"id\": 1,\n  \"name\": \"\",\n  \"duty\": \"Percent50\",\n  \"initialVolume\": 15,\n  \"envelopeIncreasing\": false,\n  \"envelopeStepFrames\": 0,\n  \"volumeMacro\": null,\n  \"arpeggioMacro\": null,\n  \"pitchMacro\": null\n}";
            var instrument = new GbPulseInstrument();
            Assert.Equal(Encoding.UTF8.GetBytes(LegacyJson), Encoding.UTF8.GetBytes(InstrumentJson.Serialize(instrument)));
            Assert.Equal(LegacyJson, InstrumentJson.Serialize(InstrumentJson.Deserialize(LegacyJson.Replace("\n}", ",\n  \"dutyMacro\": null\n}", StringComparison.Ordinal))));
            instrument.DutyMacro = new Macro { Values = new[] { 1, 2, 3, 4 }, LoopIndex = 1 };
            string json = InstrumentJson.Serialize(instrument);
            Assert.True(json.IndexOf("\"dutyMacro\"", StringComparison.Ordinal) > json.IndexOf("\"pitchMacro\"", StringComparison.Ordinal));
            var copy = Assert.IsType<GbPulseInstrument>(InstrumentJson.Deserialize(json));
            Assert.NotNull(copy.DutyMacro);
            Assert.Equal(instrument.DutyMacro.Values, copy.DutyMacro.Values);
            Assert.Equal(1, copy.DutyMacro.LoopIndex);
            Assert.NotSame(instrument.DutyMacro.Values, copy.DutyMacro.Values);
            Song song = CreateSong();
            song.Instruments[0] = instrument;
            string saved = SongSerializer.Serialize(song);
            Assert.Equal(saved, SongSerializer.Serialize(SongSerializer.Deserialize(saved)));
        }

        /// <summary>範囲外と不正ループを保存・読込の両方で拒否する。</summary>
        [Theory]
        [InlineData(0, -1)]
        [InlineData(5, -1)]
        [InlineData(1, -2)]
        [InlineData(4, 1)]
        public void JsonRejectsInvalidDutyMacros(int value, int loopIndex)
        {
            Song song = CreateSong();
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            string legacy = SongSerializer.Serialize(song);
            instrument.DutyMacro = new Macro { Values = new[] { value }, LoopIndex = loopIndex };
            Assert.Throws<SongValidationException>(() => SongSerializer.Serialize(song));
            string invalid = legacy.Replace("\"pitchMacro\": null", $"\"pitchMacro\": null, \"dutyMacro\": {{\"values\":[{value}],\"loopIndex\":{loopIndex}}}", StringComparison.Ordinal);
            Assert.Throws<SongValidationException>(() => SongSerializer.Deserialize(invalid));
        }

        /// <summary>未指定と空列は固定デューティの VGM 全バイトも維持する。</summary>
        [Theory]
        [InlineData(DutyCycle.Percent12_5)]
        [InlineData(DutyCycle.Percent25)]
        [InlineData(DutyCycle.Percent50)]
        [InlineData(DutyCycle.Percent75)]
        public void EmptyDutyPreservesLegacyVgmBytes(DutyCycle duty)
        {
            Song song = CreateSong();
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.Duty = duty;
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            AddNote(song, PulseTwoTrack, 0, song.LengthTicks);
            ControlTimelineResult legacy = CreateControl(song);
            Assert.NotNull(legacy.Timeline);
            RegisterTimeline? expected = GameBoyRegisterCompiler.Compile(legacy.Timeline, legacy.Report);
            Assert.NotNull(expected);
            instrument.DutyMacro = new Macro();
            ControlTimelineResult empty = CreateControl(song);
            Assert.NotNull(empty.Timeline);
            RegisterTimeline? actual = GameBoyRegisterCompiler.Compile(empty.Timeline, empty.Report);
            Assert.NotNull(actual);
            Assert.Equal(VgmWriterTests.Write(expected, song.Title, legacy.Report), VgmWriterTests.Write(actual, song.Title, empty.Report));
        }

        /// <summary>制御列作成後の元配列変更やマクロ解除が公開済みの列へ漏れない。</summary>
        [Fact]
        public void ControlSnapshotIsolatesDutyMacroFromSourceEdits()
        {
            Song song = CreateSong();
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            var macro = new Macro { Values = new[] { 1, 2, 3, 4 } };
            instrument.DutyMacro = macro;
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            macro.Values[0] = 4;
            macro.LoopIndex = 0;
            instrument.DutyMacro = null;
            Assert.Equal(new[] { 1, 2, 3, 4 }, control.Timeline.Events.Where(value => value.Kind != ControlEventKind.NoteOff).Select(value => value.Duty));
        }

        private static float[] RenderFrames(GbPulseInstrument instrument)
        {
            var synthesizer = new GbPulseSynthesizer(SampleRate);
            synthesizer.NoteOn(ConcertNote, FullVolume, instrument, ReadOnlySpan<NoteEffect>.Empty);
            var samples = new float[FrameSamples * FrameCount];
            for (int frame = 0; frame < FrameCount; frame++)
            {
                synthesizer.Render(samples.AsSpan(frame * FrameSamples, FrameSamples));
                synthesizer.AdvanceFrame();
            }
            return samples;
        }
    }
}
