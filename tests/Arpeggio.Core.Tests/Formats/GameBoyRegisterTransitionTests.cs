using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using static Arpeggio.Core.Tests.Formats.GameBoyRegisterTestData;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>GB の継続更新・再発音・共有 routing と DAC の副作用をレジスタ列で検証する。</summary>
    public sealed class GameBoyRegisterTransitionTests
    {
        private const int Routing = 0xFF25;
        private const int Power = 0xFF26;
        private const int PulseOneEnvelope = 0xFF12;
        private const int PulseOneLow = 0xFF13;
        private const int PulseOneHigh = 0xFF14;
        private const int PulseTwoEnvelope = 0xFF17;
        private const int PulseTwoLow = 0xFF18;
        private const int PulseTwoHigh = 0xFF19;
        private const int WaveDac = 0xFF1A;
        private const int WaveLow = 0xFF1D;
        private const int WaveHigh = 0xFF1E;
        private const int NoiseEnvelope = 0xFF21;
        private const int WaveRamStart = 0xFF30;
        private const int WaveRamEnd = 0xFF3F;
        private const int TriggerMask = 0x80;
        private const int LengthEnableMask = 0x40;
        private const int AllRouting = 0x77;
        private const int WaveRamBytes = 16;
        private const int OctaveDown = -12;

        /// <summary>high 境界を両方向に越える継続ピッチは low → high の差分だけを書き、再 trigger しない。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh, 0xD6, 6, 0xAC, 5)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh, 0xD6, 6, 0xAC, 5)]
        [InlineData(WaveTrack, WaveLow, WaveHigh, 0x6B, 7, 0xD6, 6)]
        public void ContinuingPitchWritesOnlyChangedFrequencyBits(int trackIndex, int lowAddress,
            int highAddress, int originalLow, int originalHigh, int changedLow, int changedHigh)
        {
            Song song = CreateSong();
            SetArpeggio(song, trackIndex, new[] { 0, OctaveDown, OctaveDown, 0 });
            AddNote(song, trackIndex, 0, song.LengthTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (lowAddress, changedLow), (highAddress, changedHigh) }, ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[] { (lowAddress, originalLow), (highAddress, originalHigh) }, ValuesAt(timeline, FrameSamples * 3));
            Assert.Single(timeline.Writes, write => write.Address == highAddress && (write.Value & TriggerMask) != 0);
        }

        /// <summary>register 1417 → 1673 は low が同じなので high だけを書き、length enable と trigger を立てない。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneHigh, 56)]
        [InlineData(PulseTwoTrack, PulseTwoHigh, 56)]
        [InlineData(WaveTrack, WaveHigh, 44)]
        public void HighOnlyPitchChangeDoesNotRewriteLow(int trackIndex, int highAddress, int midiNote)
        {
            const int PitchOffset = 9;
            Song song = CreateSong();
            SetArpeggio(song, trackIndex, new[] { 0, PitchOffset });
            AddNote(song, trackIndex, 0, song.LengthTicks, midiNote);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (highAddress, 6) }, ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
        }

        /// <summary>小さい vibrato は high を変えず、同音の隣接 On では同値の trigger を省略しない。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneLow, PulseOneHigh)]
        [InlineData(PulseTwoTrack, PulseTwoLow, PulseTwoHigh)]
        [InlineData(WaveTrack, WaveLow, WaveHigh)]
        public void VibratoChangesLowAndAdjacentNoteForcesTrigger(int trackIndex, int lowAddress, int highAddress)
        {
            const int FirstDuration = FrameTicks * 10;
            const int VibratoCents = 10;
            Song song = CreateSong(FirstDuration + FrameTicks);
            Note first = AddNote(song, trackIndex, 0, FirstDuration);
            first.Effects = new[] { new NoteEffect(NoteEffectKind.Vibrato, VibratoCents) };
            AddNote(song, trackIndex, FirstDuration, FrameTicks);
            RegisterTimeline timeline = Compile(song);
            long secondOnset = FirstDuration / FrameTicks * FrameSamples;
            Assert.Equal(new long[] { 0, secondOnset }, timeline.Writes.Where(write => write.Address == highAddress &&
                (write.Value & TriggerMask) != 0).Select(write => write.PositionSamples));
            Assert.DoesNotContain(timeline.Writes, write => write.Address == highAddress && write.PositionSamples > 0 && write.PositionSamples < secondOnset);
            Assert.Contains(timeline.Writes, write => write.Address == lowAddress && write.PositionSamples > 0 && write.PositionSamples < secondOnset);
        }

        /// <summary>Pulse の音量と周期が同時に変わる場合、新周期を先に書き、他声の routing を保って再起動する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, PulseOneLow, PulseOneHigh, 0x66)]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, PulseTwoLow, PulseTwoHigh, 0x55)]
        public void PositiveVolumeChangeSetsNewPitchBeforeRetrigger(int trackIndex, int envelopeAddress,
            int lowAddress, int highAddress, int otherRouting)
        {
            Song song = CreateSong();
            AddOtherVoices(song, trackIndex);
            var pulse = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            pulse.VolumeMacro = new Macro { Values = new[] { 15, 9, 9, 15 } };
            pulse.ArpeggioMacro = new Macro { Values = new[] { 0, OctaveDown, OctaveDown, 0 } };
            AddNote(song, trackIndex, 0, song.LengthTicks);
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = GameBoyRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new[]
            {
                (Routing, otherRouting), (envelopeAddress, 0), (lowAddress, 0xAC), (highAddress, 5),
                (envelopeAddress, 0x90), (highAddress, 0x85), (Routing, AllRouting)
            }, ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("EnvelopeRetriggered", warning.Code);
            Assert.Equal(trackIndex, warning.SourceTrack);
            Assert.Equal(2L, warning.OccurrenceCount);
        }

        /// <summary>音量 0 では DAC と自声 routing を落とし、保持中の余分な書き込みを避けて正音量へ復帰する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, PulseOneHigh, 0x66)]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, PulseTwoHigh, 0x55)]
        public void ZeroVolumeRecoveryRetriggersWithoutStoppingOtherVoices(int trackIndex, int envelopeAddress,
            int highAddress, int otherRouting)
        {
            Song song = CreateSong();
            AddOtherVoices(song, trackIndex);
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).VolumeMacro = new Macro { Values = new[] { 15, 0, 0, 9 } };
            AddNote(song, trackIndex, 0, song.LengthTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[] { (envelopeAddress, 0), (Routing, otherRouting) }, ValuesAt(timeline, FrameSamples));
            Assert.Empty(ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[]
            {
                (envelopeAddress, 0), (envelopeAddress, 0x90), (highAddress, 0x86), (Routing, AllRouting)
            }, ValuesAt(timeline, FrameSamples * 3));
        }

        /// <summary>開始音量 0 は trigger せず、初めて正音量になった制御フレームで DAC と routing を有効にする。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, PulseOneHigh, 0x11)]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, PulseTwoHigh, 0x22)]
        public void SilentOnsetWaitsForPositiveVolumeBeforeTrigger(int trackIndex, int envelopeAddress,
            int highAddress, int routing)
        {
            Song song = CreateSong();
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).VolumeMacro = new Macro { Values = new[] { 0, 15 } };
            AddNote(song, trackIndex, 0, song.LengthTicks);
            RegisterTimeline timeline = Compile(song);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples == 0 &&
                ((write.Address == highAddress && (write.Value & TriggerMask) != 0) || (write.Address == Routing && write.Value != 0)));
            Assert.Equal(new[]
            {
                (envelopeAddress, 0), (envelopeAddress, 0xF0), (highAddress, 0x86), (Routing, routing)
            }, ValuesAt(timeline, FrameSamples));
        }

        /// <summary>同時交代は全 Off を先に書き、発音順はトラック番号順で length enable を常に無効に保つ。</summary>
        [Fact]
        public void SimultaneousReplacementStopsAllBeforeAnyTrigger()
        {
            Song song = CreateSong();
            for (int trackIndex = PulseOneTrack; trackIndex <= WaveTrack; trackIndex++)
            {
                AddNote(song, trackIndex, 0, FrameTicks);
                AddNote(song, trackIndex, FrameTicks, FrameTicks);
            }
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[]
            {
                (PulseOneEnvelope, 0), (Routing, 0x66), (PulseTwoEnvelope, 0), (Routing, 0x44), (WaveDac, 0), (Routing, 0)
            }, ValuesAt(timeline, FrameSamples).Take(6));
            int[] highAddresses = { PulseOneHigh, PulseTwoHigh, WaveHigh };
            Assert.Equal(highAddresses, timeline.Writes.Where(write => write.PositionSamples == FrameSamples &&
                highAddresses.Contains(write.Address) && (write.Value & TriggerMask) != 0).Select(write => (int)write.Address));
            Assert.All(timeline.Writes.Where(write => highAddresses.Contains(write.Address)),
                write => Assert.Equal(0, write.Value & LengthEnableMask));
        }

        /// <summary>Wave の RAM は各 On で DAC off の間だけ全 byte を書き、trigger 時には DAC が有効になっている。</summary>
        [Fact]
        public void EveryWaveOnsetReloadsRamWithDacDisabled()
        {
            Song song = CreateSong();
            AddNote(song, WaveTrack, 0, FrameTicks);
            AddNote(song, WaveTrack, FrameTicks, FrameTicks);
            RegisterTimeline timeline = Compile(song);
            bool dacEnabled = false;
            int routing = 0;
            int ramWrites = 0;
            int triggers = 0;
            foreach (RegisterWrite write in timeline.Writes)
            {
                if (write.Address == Routing)
                {
                    routing = write.Value;
                }
                if (write.Address == WaveDac)
                {
                    dacEnabled = (write.Value & TriggerMask) != 0;
                }
                if (write.Address >= WaveRamStart && write.Address <= WaveRamEnd)
                {
                    Assert.False(dacEnabled);
                    Assert.Equal(0, routing);
                    Assert.Equal(WaveRamStart + ramWrites, write.Address);
                    ramWrites++;
                }
                if (write.Address == WaveHigh && (write.Value & TriggerMask) != 0)
                {
                    Assert.True(dacEnabled);
                    Assert.Equal(WaveRamBytes, ramWrites);
                    Assert.Equal(0, routing);
                    ramWrites = 0;
                    triggers++;
                }
            }
            Assert.Equal(2, triggers);
            Assert.False(dacEnabled);
        }

        /// <summary>個別 Off は左右を含む他声の routing を維持する。</summary>
        [Theory]
        [InlineData(PulseOneTrack, PulseOneEnvelope, 0x46)]
        [InlineData(PulseTwoTrack, PulseTwoEnvelope, 0x54)]
        [InlineData(WaveTrack, WaveDac, 0x12)]
        public void IndividualOffPreservesOtherVoices(int stoppedTrack, int dacAddress, int remainingRouting)
        {
            Song song = CreateSong();
            song.Tracks[PulseOneTrack].Pan = -1;
            song.Tracks[PulseTwoTrack].Pan = 1;
            for (int trackIndex = PulseOneTrack; trackIndex <= WaveTrack; trackIndex++)
            {
                AddNote(song, trackIndex, 0, trackIndex == stoppedTrack ? FrameTicks : song.LengthTicks);
            }
            Assert.Equal(new[] { (dacAddress, 0), (Routing, remainingRouting) }, ValuesAt(Compile(song), FrameSamples));
        }

        /// <summary>空曲を含む有限終端で routing と全 DAC を止め、NR52 の再リセットを使わない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FiniteEndExplicitlySilencesAllChannels(bool addNotes)
        {
            const int TerminalWrites = 5;
            Song song = CreateSong();
            if (addNotes)
            {
                for (int trackIndex = PulseOneTrack; trackIndex <= WaveTrack; trackIndex++)
                {
                    AddNote(song, trackIndex, 0, song.LengthTicks);
                }
            }
            RegisterTimeline timeline = Compile(song);
            Assert.Equal(new[]
            {
                (Routing, 0), (PulseOneEnvelope, 0), (PulseTwoEnvelope, 0), (NoiseEnvelope, 0), (WaveDac, 0)
            }, ValuesAt(timeline, timeline.EndSamples).TakeLast(TerminalWrites));
            Assert.All(timeline.Writes.Where(write => write.Address == Power), write => Assert.Equal(0L, write.PositionSamples));
        }

        /// <summary>有限二周は loopStartTick から同じ書き込みを再発音し、初期化を繰り返さない。</summary>
        [Fact]
        public void FiniteLoopRetriggersWithoutPowerReset()
        {
            const int LengthTicks = FrameTicks * 3;
            Song song = CreateSong(LengthTicks);
            song.LoopStartTick = FrameTicks;
            AddNote(song, PulseOneTrack, FrameTicks, FrameTicks);
            AddNote(song, WaveTrack, FrameTicks, FrameTicks);
            RegisterTimeline timeline = Compile(song, loops: 2);
            Assert.Equal(5L * FrameSamples, timeline.EndSamples);
            Assert.Equal(ValuesAt(timeline, FrameSamples), ValuesAt(timeline, FrameSamples * 3));
            Assert.Equal(ValuesAt(timeline, FrameSamples * 2), ValuesAt(timeline, FrameSamples * 4));
            Assert.Equal(2, timeline.Writes.Count(write => write.Address == Power));
        }

        private static void SetArpeggio(Song song, int trackIndex, int[] values)
        {
            var macro = new Macro { Values = values };
            if (trackIndex == WaveTrack)
            {
                Assert.IsType<GbWaveInstrument>(song.Instruments[1]).ArpeggioMacro = macro;
                return;
            }
            Assert.IsType<GbPulseInstrument>(song.Instruments[0]).ArpeggioMacro = macro;
        }

        private static void AddOtherVoices(Song song, int changingTrack)
        {
            const int OtherPulseInstrument = 3;
            song.Instruments.Add(new GbPulseInstrument { Id = OtherPulseInstrument });
            int otherTrack = changingTrack == PulseOneTrack ? PulseTwoTrack : PulseOneTrack;
            AddNote(song, otherTrack, 0, song.LengthTicks).InstrumentId = OtherPulseInstrument;
            AddNote(song, WaveTrack, 0, song.LengthTicks);
        }
    }
}
