using System;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>先頭マクロ・終端保持・ループと複合ノート効果を固定値で検証する。</summary>
    public sealed class ControlModulationTests
    {
        /// <summary>空マクロは既定値を使い、再発音は以前の効果とフレームを引き継がない。</summary>
        [Fact]
        public void EmptyMacrosUseFallbacksAndRestartClearsEffects()
        {
            var modulation = new VoiceModulation();
            modulation.Start(60, 15, new[] { new NoteEffect(NoteEffectKind.PitchSlide, 12) });
            var empty = new Macro();
            modulation.Configure(empty, empty, empty, empty, initialDuty: (int)DutyCycle.Percent25);
            modulation.SetDuration(1);
            Assert.Equal(60.0, modulation.MidiNote);
            Assert.Equal(1.0, modulation.Volume);
            Assert.Equal((int)DutyCycle.Percent25, modulation.Duty);
            modulation.AdvanceFrame();
            Assert.Equal(60.2, modulation.MidiNote, precision: 10);
            modulation.Start(72, 3, ReadOnlySpan<NoteEffect>.Empty);
            modulation.Configure(null, null, null, initialDuty: (int)DutyCycle.Percent50);
            modulation.SetDuration(1);
            modulation.AdvanceFrame();
            Assert.Equal(72.0, modulation.MidiNote);
            Assert.Equal(0.2, modulation.Volume, precision: 10);
        }

        /// <summary>マクロのループと終端保持、スライド・ビブラート・ノートアルペジオを同時に適用する。</summary>
        [Fact]
        public void CombinedEffectsAndMacroLoopsMatchFixedFrameValues()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 8);
            var instrument = Assert.IsType<NesPulseInstrument>(song.Instruments[0]);
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 10 } };
            instrument.ArpeggioMacro = new Macro { Values = new[] { 0, 4, 7 }, LoopIndex = 1 };
            instrument.PitchMacro = new Macro { Values = new[] { 0, 50, -50 } };
            instrument.DutyMacro = new Macro { Values = new[] { 1, 4 } };
            song.Tracks[0].Notes.Add(new Note
            {
                DurationTicks = 8,
                Effects = new[]
                {
                    new NoteEffect(NoteEffectKind.PitchSlide, 12),
                    new NoteEffect(NoteEffectKind.VolumeSlide, -6),
                    new NoteEffect(NoteEffectKind.Vibrato, 100),
                    new NoteEffect(NoteEffectKind.Arpeggio, 0x37)
                }
            });
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(result.Timeline);
            ControlEvent[] states = result.Timeline.Events.Where(control => control.Kind != ControlEventKind.NoteOff).ToArray();
            Assert.Equal(new long[] { 0, 735, 1470, 2205 }, states.Select(control => control.PositionSamples));
            Assert.Equal(60.0, states[0].MidiNote);
            Assert.Equal(70.5 + Math.Sin(Math.PI / 5), states[1].MidiNote, precision: 10);
            Assert.Equal(79.5 + Math.Sin(2 * Math.PI / 5), states[2].MidiNote, precision: 10);
            Assert.Equal(72.5 + Math.Sin(3 * Math.PI / 5), states[3].MidiNote, precision: 10);
            Assert.Equal(0.6, states[1].Volume, precision: 10);
            Assert.Equal(12.0 * 10 / 225, states[2].Volume, precision: 10);
            Assert.Equal(new[] { 1, 4, 4, 4 }, states.Select(control => control.Duty));
        }

        /// <summary>GB Pulse は共通音量を保持し、後段の envelope 計算用に発音フレームを返す。</summary>
        [Fact]
        public void GameBoyEnvelopeIsSeparateFromCommonModulation()
        {
            Song song = SongFactory.Create(ChipKind.GameBoy, lengthTicks: 8);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            instrument.InitialVolume = 9;
            instrument.EnvelopeStepFrames = 2;
            instrument.EnvelopeIncreasing = false;
            instrument.VolumeMacro = new Macro { Values = new[] { 15, 10 } };
            song.Tracks[0].Notes.Add(new Note { DurationTicks = 8, Volume = 12 });
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(result.Timeline);
            ControlInstrument captured = result.Timeline.Instruments[1];
            Assert.Equal(9, captured.InitialVolume);
            Assert.Equal(2, captured.EnvelopeStepFrames);
            Assert.False(captured.EnvelopeIncreasing);
            ControlEvent update = Assert.Single(result.Timeline.Events, control => control.Frame == 2 && control.Kind == ControlEventKind.Update);
            Assert.Equal(12.0 * 10 / 225, update.Volume, precision: 10);
            Assert.Equal(3, update.Duty);
        }

        /// <summary>Triangle / Wave と両 Noise のマクロ配線をチップ固有量子化前の値で検証する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ChannelKind.Triangle, 2)]
        [InlineData(ChipKind.Nes, ChannelKind.Noise, 3)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Wave, 2)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Noise, 3)]
        public void NonPulseChannelsUseTheirOwnMacroConfiguration(ChipKind chip, ChannelKind channel, int trackIndex)
        {
            Song song = SongFactory.Create(chip, lengthTicks: 4);
            var pitch = new Macro { Values = new[] { 100 } };
            var volume = new Macro { Values = new[] { 5 } };
            Instrument instrument = channel switch
            {
                ChannelKind.Triangle => new NesTriangleInstrument { PitchMacro = pitch },
                ChannelKind.Wave => new GbWaveInstrument { PitchMacro = pitch, OutputLevel = 25 },
                _ => chip == ChipKind.Nes
                    ? new NesNoiseInstrument { PitchMacro = pitch, VolumeMacro = volume, NoiseMode = NoiseMode.Short }
                    : new GbNoiseInstrument { PitchMacro = pitch, VolumeMacro = volume, LfsrWidth = 7 }
            };
            instrument.Id = 2;
            song.Instruments.Add(instrument);
            song.Tracks[trackIndex].Notes.Add(new Note { DurationTicks = 4, InstrumentId = 2 });
            ControlTimelineResult result = ControlTimeline.Create(song, new ChipExportOptions { Format = ConversionFormat.Vgm });
            Assert.NotNull(result.Timeline);
            ControlEvent onset = Assert.Single(result.Timeline.Events, control => control.Kind == ControlEventKind.NoteOn);
            Assert.Equal(61.0, onset.MidiNote);
            Assert.Equal(channel == ChannelKind.Noise ? 1.0 / 3 : 1.0, onset.Volume, precision: 10);
            ControlInstrument captured = result.Timeline.Instruments[2];
            Assert.Equal(instrument.Kind, captured.Kind);
            if (instrument is NesNoiseInstrument)
            {
                Assert.Equal(NoiseMode.Short, captured.NoiseMode);
            }
            if (instrument is GbNoiseInstrument)
            {
                Assert.Equal(7, captured.LfsrWidth);
            }
        }
    }
}
