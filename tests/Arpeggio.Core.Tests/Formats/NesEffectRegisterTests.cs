using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>全ノート効果とマクロを NES レジスタの手計算値へ接続する。</summary>
    public sealed class NesEffectRegisterTests
    {
        private const int PulseTrack = 0;
        private const int NoiseTrack = 3;
        private const int NoiseInstrument = 2;
        private const int FrameTicks = 2;
        private const int FrameSamples = 735;
        private const int SongTicks = FrameTicks * 5;
        private const int Status = 0x4015;
        private const int PulseControl = 0x4000;
        private const int PulseLow = 0x4002;
        private const int PulseHigh = 0x4003;
        private const int NoiseControl = 0x400C;
        private const int NoisePeriod = 0x400E;
        private const int NoiseLength = 0x400F;

        /// <summary>Delay・両スライド・vibrato・ノート arpeggio と全 Pulse マクロを合成した固定列を返す。</summary>
        [Fact]
        public void AllPulseEffectsProduceFixedRegistersAfterDelay()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: SongTicks);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 10 } };
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 4, 7 }, LoopIndex = 1 };
            instrument.PitchMacro = new Macro { Values = new[] { 0, 50, -50 } };
            instrument.DutyMacro = new Macro { Values = new[] { 1, 4 } };
            song.Tracks[PulseTrack].Notes.Add(CreateNote(1));
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new[] { (Status, 1), (PulseControl, 0x3F), (PulseLow, 0xAB), (PulseHigh, 1) }, ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (PulseControl, 0xF9), (PulseLow, 0xE0), (PulseHigh, 0) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[] { (PulseControl, 0xF8), (PulseLow, 0x82) }, ValuesAt(timeline, FrameSamples * 3));
            Assert.Equal(new[] { (PulseControl, 0xF7), (PulseLow, 0xC4) }, ValuesAt(timeline, FrameSamples * 4));
            ConversionDiagnostic warning = Assert.Single(control.Report.Warnings);
            Assert.Equal("PulsePhaseRestarted", warning.Code);
            Assert.Equal(0L, warning.SourceTick);
            Assert.Equal(1L, warning.OccurrenceCount);
            Assert.DoesNotContain(timeline.Writes, write => write.PositionSamples == 0 && write.Address == PulseHigh);
            Assert.Equal(FrameSamples * 5L, timeline.EndSamples);
        }

        /// <summary>Noise も全ノート効果を合成して selection を選び、継続で length を再ロードしない。</summary>
        [Fact]
        public void AllNoiseEffectsProduceFixedSelectionAndVolumeRegisters()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: SongTicks);
            song.Instruments.Add(new NesNoiseInstrument
            {
                Id = NoiseInstrument, NoiseMode = NoiseMode.Short,
                VolumeMacro = new Macro { Values = new[] { 15, 10 } },
                PitchMacro = new Macro { Values = new[] { 0, 50, -50 } }
            });
            song.Tracks[NoiseTrack].Notes.Add(CreateNote(NoiseInstrument));
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new[] { (Status, 8), (NoiseControl, 0x3F), (NoisePeriod, 0x8C), (NoiseLength, 0) }, ValuesAt(timeline, FrameSamples));
            Assert.Equal(new[] { (NoiseControl, 0x39), (NoisePeriod, 0x83) }, ValuesAt(timeline, FrameSamples * 2));
            Assert.Equal(new[] { (NoiseControl, 0x38), (NoisePeriod, 0x89) }, ValuesAt(timeline, FrameSamples * 3));
            Assert.Equal(new[] { (NoiseControl, 0x37), (NoisePeriod, 0x85) }, ValuesAt(timeline, FrameSamples * 4));
            Assert.Single(timeline.Writes, write => write.Address == NoiseLength);
            Assert.Empty(control.Report.Warnings);
        }

        /// <summary>Noise の PitchMacro LoopIndex は指定位置へ戻り、非ループ音量は最後の値を保持する。</summary>
        [Fact]
        public void NoiseMacroLoopAndVolumeEndHoldProduceOnlyChangedRegisters()
        {
            Song song = SongFactory.Create(ChipKind.Nes, tempoBpm: 150, lengthTicks: SongTicks);
            song.Instruments.Add(new NesNoiseInstrument
            {
                Id = NoiseInstrument,
                PitchMacro = new Macro { Values = new[] { 0, 100, 200 }, LoopIndex = 1 },
                VolumeMacro = new Macro { Values = new[] { 15, 10 } }
            });
            song.Tracks[NoiseTrack].Notes.Add(new Note { DurationTicks = SongTicks, MidiNote = 0, InstrumentId = NoiseInstrument });
            ControlTimelineResult control = CreateControl(song);
            Assert.NotNull(control.Timeline);
            RegisterTimeline? timeline = NesRegisterCompiler.Compile(control.Timeline, control.Report);
            Assert.NotNull(timeline);
            Assert.Equal(new byte[] { 0, 1, 2, 1, 2 }, timeline.Writes.Where(write => write.Address == NoisePeriod).Select(write => write.Value));
            Assert.Equal(new byte[] { 0x3F, 0x3A, 0x30 }, timeline.Writes.Where(write => write.Address == NoiseControl).Select(write => write.Value));
            Assert.Single(timeline.Writes, write => write.Address == NoiseLength);
            Assert.Empty(control.Report.Warnings);
        }

        private static Note CreateNote(int instrumentId)
            => new Note
            {
                DurationTicks = SongTicks, MidiNote = 60, InstrumentId = instrumentId,
                Effects = new[]
                {
                    new NoteEffect(NoteEffectKind.Delay, FrameTicks),
                    new NoteEffect(NoteEffectKind.PitchSlide, 12),
                    new NoteEffect(NoteEffectKind.VolumeSlide, -6),
                    new NoteEffect(NoteEffectKind.Vibrato, 100),
                    new NoteEffect(NoteEffectKind.Arpeggio, 0x37)
                }
            };

        private static ControlTimelineResult CreateControl(Song song)
            => ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });

        private static (int Address, int Value)[] ValuesAt(RegisterTimeline timeline, long positionSamples)
            => timeline.Writes.Where(write => write.PositionSamples == positionSamples)
                .Select(write => ((int)write.Address, (int)write.Value)).ToArray();
    }
}
