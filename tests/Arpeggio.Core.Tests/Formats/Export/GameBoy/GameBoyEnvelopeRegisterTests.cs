using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using static Arpeggio.Core.Tests.Formats.Export.GameBoy.GameBoyRegisterTestData;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.GameBoy;

namespace Arpeggio.Core.Tests.Formats.Export.GameBoy
{
    /// <summary>GB のソフトウェア envelope・明示再 trigger・共有 routing を PCM なしで検証する。</summary>
    public sealed class GameBoyEnvelopeRegisterTests
    {
        private const int NoiseInstrumentId = 3;
        private const int OtherPulseInstrumentId = 4;
        private const int PulseOneEnvelope = 0xFF12;
        private const int PulseOneHigh = 0xFF14;
        private const int PulseTwoEnvelope = 0xFF17;
        private const int PulseTwoHigh = 0xFF19;
        private const int NoiseEnvelope = 0xFF21;
        private const int NoiseFrequency = 0xFF22;
        private const int NoiseTrigger = 0xFF23;
        private const int Routing = 0xFF25;
        private const int Power = 0xFF26;
        private const int AllRouting = 0xFF;
        private const int VolumeShift = 4;
        private const int TriggerMask = 0x80;
        private const int EnvelopePaceMask = 0x0F;

        /// <summary>両 Pulse の envelope は整数フレーム間隔で増減し、0／15 の端と step=0 を保持する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, false, 2, 2, new[] { 2, 2, 1, 1, 0, 0, 0 })]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, false, 2, 2, new[] { 2, 2, 1, 1, 0, 0, 0 })]
        [InlineData(PulseOneTrack, PulseOneEnvelope, true, 13, 2, new[] { 13, 13, 14, 14, 15, 15, 15 })]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, true, 13, 2, new[] { 13, 13, 14, 14, 15, 15, 15 })]
        [InlineData(PulseOneTrack, PulseOneEnvelope, true, 0, 1, new[] { 0, 1, 2, 3, 4, 5, 6 })]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, true, 0, 1, new[] { 0, 1, 2, 3, 4, 5, 6 })]
        [InlineData(PulseOneTrack, PulseOneEnvelope, false, 9, 0, new[] { 9, 9, 9, 9, 9, 9, 9 })]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, true, 9, 0, new[] { 9, 9, 9, 9, 9, 9, 9 })]
        [InlineData(PulseOneTrack, PulseOneEnvelope, false, 9, 3, new[] { 9, 9, 9, 8, 8, 8, 7 })]
        public void EnvelopeStepsClampAndDisabledEnvelopeHolds(int trackIndex, int envelopeAddress,
            bool increasing, int initialVolume, int stepFrames, int[] expectedVolumes)
        {
            Song song = CreateSong(expectedVolumes.Length * FrameTicks);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = initialVolume;
            instrument.EnvelopeIncreasing = increasing;
            instrument.EnvelopeStepFrames = stepFrames;
            AddNote(song, trackIndex, 0, song.LengthTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            int retriggers = 0;
            for (int frame = 0; frame < expectedVolumes.Length; frame++)
            {
                int volume = timeline.Writes.Last(write => write.Address == envelopeAddress && write.PositionSamples <= frame * FrameSamples).Value >> VolumeShift;
                Assert.Equal(expectedVolumes[frame], volume);
                if (frame > 0 && expectedVolumes[frame] == expectedVolumes[frame - 1])
                {
                    Assert.Empty(ValuesAt(timeline, frame * FrameSamples));
                }
                if (frame > 0 && expectedVolumes[frame] > 0 && expectedVolumes[frame] != expectedVolumes[frame - 1])
                {
                    retriggers++;
                }
            }
            Assert.All(timeline.Writes.Where(write => write.Address == envelopeAddress), write => Assert.Equal(0, write.Value & EnvelopePaceMask));
            Assert.Equal(retriggers, control.Report.WarningCount);
            Assert.All(control.Report.Warnings, warning => Assert.Equal("EnvelopeRetriggered", warning.Code));
        }

        /// <summary>時間 envelope とノート・マクロ・VolumeSlide の積を同じフレームで確定し、最終整数音量だけで再 trigger を決める。</summary>
        [Fact]
        public void EnvelopeMultipliesMacrosAndSlideBeforeRounding()
        {
            Song song = CreateSong(FrameTicks * 4);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = 10;
            instrument.EnvelopeStepFrames = 1;
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 10, 10, 15 } };
            Note note = AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            note.Volume = 9;
            note.Effects = new[] { new NoteEffect(NoteEffectKind.VolumeSlide, 6) };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            // (9,10.5,12,13.5) × (15,10,10,15) × (10,9,8,7) / 225 = (6,4.2,4.266...,6.3)。
            Assert.Equal(new[] { 0x60, 0x40, 0x60 }, timeline.Writes.Where(write => write.Address == PulseOneEnvelope && write.Value > 0).Select(write => (int)write.Value));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(3L, control.Report.WarningCountsByCode["VolumeQuantized"]);
            Assert.Equal(2L, control.Report.WarningCountsByCode["EnvelopeRetriggered"]);
        }

        /// <summary>両 Pulse の時間 envelope 再起動では他の三声を維持し、NRx2 の設定から trigger 後の routing 復元までを固定する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, PulseOneHigh, 0xEE)]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, PulseTwoHigh, 0xDD)]
        public void TimedEnvelopeRetriggerPreservesOtherThreeVoices(int trackIndex, int envelopeAddress,
            int triggerAddress, int otherRouting)
        {
            Song song = CreateFourVoiceSong(trackIndex);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.EnvelopeStepFrames = 1;
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new[]
            {
                (Routing, otherRouting), (envelopeAddress, 0), (envelopeAddress, 0xE0),
                (triggerAddress, 0x86), (Routing, AllRouting)
            }, ValuesAt(timeline, FrameSamples));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("EnvelopeRetriggered", warning.Code);
            Assert.Equal(trackIndex, warning.SourceTrack);
            Assert.Equal(3L, warning.OccurrenceCount);
            Assert.All(timeline.Writes.Where(write => write.Address == Power), write => Assert.Equal(0L, write.PositionSamples));
        }

        /// <summary>Noise の正音量変更・ゼロ音量・復帰で他声を保ち、周期を trigger より先に書く。</summary>
        [Fact]
        public void NoiseVolumeAndPitchChangesPreserveOtherVoicesAndResetLfsrExplicitly()
        {
            Song song = CreateFourVoiceSong(NoiseTrack);
            var noise = Assert.IsType<GbNoiseInstrument>(song.Instruments[2]);
            noise.VolumeMacro = new Macro { Values = new[] { 15, 9, 0, 9 } };
            noise.PitchMacro = new Macro { Values = new[] { 0, 100, 100, 200 } };
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new[]
            {
                (Routing, 0x77), (NoiseEnvelope, 0), (NoiseFrequency, 0x02),
                (NoiseEnvelope, 0x90), (NoiseTrigger, 0x80), (Routing, AllRouting)
            }, ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (NoiseEnvelope, 0), (Routing, 0x77) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[]
            {
                (NoiseEnvelope, 0), (NoiseFrequency, 0x03), (NoiseEnvelope, 0x90),
                (NoiseTrigger, 0x80), (Routing, AllRouting)
            }, ValuesAt(timeline, FrameSamples * 3));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("EnvelopeRetriggered", warning.Code);
            Assert.Equal(2L, warning.OccurrenceCount);
            Assert.Contains("LFSR", warning.Message);
            Assert.Equal(NoiseTrack, warning.SourceTrack);
        }

        /// <summary>Noise の無音 On と連続するゼロ音量では trigger せず、初回の正音量で他声とともに鳴らす。</summary>
        [Fact]
        public void SilentNoiseOnsetAndHeldZeroWaitForPositiveVolume()
        {
            Song song = CreateFourVoiceSong(NoiseTrack);
            Assert.IsType<GbNoiseInstrument>(song.Instruments[2]).VolumeMacro = new Macro { Values = new[] { 0, 0, 9, 9 } };
            RegisterTimeline timeline = Compile(song);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples == 0 && write.Address == NoiseTrigger);
            Assert.Empty(ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (NoiseEnvelope, 0), (NoiseEnvelope, 0x90), (NoiseTrigger, 0x80), (Routing, AllRouting) },
                ValuesAt(timeline, FrameSamples * 2));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 3));
        }

        /// <summary>全四声の個別 Off は他声の左右 routing と DAC を変更しない。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, 0xC6)]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, 0xD5)]
        [InlineData(WaveTrack, 0xFF1A, 0x93)]
        [InlineData(NoiseTrack, NoiseEnvelope, 0x57)]
        public void IndividualOffPreservesEveryOtherVoice(int trackIndex, int dacAddress, int otherRouting)
        {
            Song song = CreateFourVoiceSong(trackIndex);
            song.Tracks[PulseTwoTrack].Pan = 1;
            song.Tracks[NoiseTrack].Pan = -1;
            song.Tracks[trackIndex].Notes[0].DurationTicks = FrameTicks;
            Assert.Equal(new[] { (dacAddress, 0), (Routing, otherRouting) }, ValuesAt(Compile(song), FrameSamples));
        }

        /// <summary>Delay 後のフレーム途中 On から次の global 境界で進み、隣接ノートでは旧更新を捨てて E を初期化する。</summary>
        [Fact]
        public void DelayedOnsetUsesGlobalFramesAndAdjacentOnsetResetsEnvelope()
        {
            Song song = CreateSong(FrameTicks * 4);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = 9;
            instrument.EnvelopeStepFrames = 1;
            AddNote(song, PulseOneTrack, 0, FrameTicks * 2).Effects = new[] { new NoteEffect(NoteEffectKind.Delay, 1) };
            AddNote(song, PulseOneTrack, FrameTicks * 2, FrameTicks * 2);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (368L, 0x90), (735L, 0x80), (1470L, 0x90), (2205L, 0x80) },
                timeline.Writes.Where(write => write.Address == PulseOneEnvelope && write.Value > 0)
                    .Select(write => (write.PositionSamples, (int)write.Value)));
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples == FrameSamples * 2 &&
                write.Address == PulseOneEnvelope && write.Value == 0x70);
        }

        /// <summary>周回でノートを途中から再発音しても E は初期値へ戻り、有限終端には更新 trigger を出さない。</summary>
        [Fact]
        public void LoopRestartResetsEnvelopeAndEndDoesNotRetrigger()
        {
            Song song = CreateSong(FrameTicks * 3);
            song.LoopStartTick = FrameTicks;
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = 9;
            instrument.EnvelopeStepFrames = 1;
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            RegisterTimeline timeline = Compile(song, loops: 2);
            Assert.Equal(new[] { 0x90, 0x80, 0x70, 0x90, 0x80 }, timeline.Writes.Where(write => write.Address == PulseOneEnvelope && write.Value > 0).Select(write => (int)write.Value));
            Assert.Equal(FrameSamples * 5L, timeline.EndSamples);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples == timeline.EndSamples &&
                write.Address == PulseOneHigh && (write.Value & TriggerMask) != 0);
        }

        /// <summary>時間 envelope の警告は明細を捨てても strict で拒否する。</summary>
        [Fact]
        public void StrictRejectsTimedRetriggersWithoutDetails()
        {
            Song song = CreateSong();
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).EnvelopeStepFrames = 1;
            AddNote(song, PulseOneTrack, 0, song.LengthTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy, strict: true, diagnosticDetailLimit: 0);
            Assert.Null(GameBoyRegisterCompiler.Compile(control.Timeline, report));
            Assert.Empty(report.Warnings);
            Assert.Equal(3L, report.WarningCountsByCode["EnvelopeRetriggered"]);
            Assert.Equal(3L, report.DroppedWarningCount);
        }

        /// <summary>確定後の元音色編集・別曲の変換で Noise 幅や時間 envelope を変えず、元ドキュメントへ書き戻さない。</summary>
        [Fact]
        public void NoiseAndEnvelopeUseIsolatedInstrumentSnapshot()
        {
            Song song = CreateFourVoiceSong(PulseOneTrack);
            var pulse = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            pulse.EnvelopeStepFrames = 1;
            var noise = Assert.IsType<GbNoiseInstrument>(song.Instruments[2]);
            var noiseVolume = new Macro { Values = new[] { 15, 9 } };
            noise.VolumeMacro = noiseVolume;
            string originalJson = SongSerializer.Serialize(song);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? first = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(first);
            Assert.Equal(originalJson, SongSerializer.Serialize(song));
            pulse.InitialVolume = 0;
            pulse.EnvelopeIncreasing = true;
            pulse.EnvelopeStepFrames = 3;
            noise.LfsrWidth = 7;
            noiseVolume.Values[0] = 0;
            Assert.NotNull(Compile(song));
            var report = new ConversionReport(ConversionFormat.Vgm, ChipKind.GameBoy);
            RegisterTimeline? repeated = GameBoyRegisterCompiler.Compile(control.Timeline, report);
            Assert.NotNull(repeated);
            Assert.Equal(first.Writes.ToArray(), repeated.Writes.ToArray());
            Assert.Equal(control.Report.WarningCount, report.WarningCount);
        }

        private static Song CreateFourVoiceSong(int changingTrack)
        {
            Song song = CreateSong();
            song.Instruments.Add(new GbNoiseInstrument { Id = NoiseInstrumentId });
            song.Instruments.Add(new GbPulseInstrument { Id = OtherPulseInstrumentId });
            for (int trackIndex = PulseOneTrack; trackIndex <= NoiseTrack; trackIndex++)
            {
                Note note = AddNote(song, trackIndex, 0, song.LengthTicks, trackIndex == NoiseTrack ? 120 : ConcertNote);
                if (trackIndex == NoiseTrack)
                {
                    note.InstrumentId = NoiseInstrumentId;
                }
                else if (trackIndex != WaveTrack && trackIndex != changingTrack)
                {
                    note.InstrumentId = OtherPulseInstrumentId;
                }
            }
            return song;
        }
    }
}
