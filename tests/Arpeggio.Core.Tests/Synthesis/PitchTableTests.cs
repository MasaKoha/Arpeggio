using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Synthesis;
using Arpeggio.Core.Synthesis.Nes;
using Arpeggio.Core.Tests.Analysis;
using Xunit;

namespace Arpeggio.Core.Tests.Synthesis
{
    /// <summary>チップ固有の周期量子化と発音範囲の境界を検証する。</summary>
    public sealed class PitchTableTests
    {
        private const int MinimumMidiNote = 0;
        private const int MaximumMidiNote = 127;
        private const double NumericalTolerance = 0.0000001;

        /// <summary>各ピッチ付きチャンネルで基準音の量子化誤差が 2 % 以内に収まる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ChannelKind.Pulse)]
        [InlineData(ChipKind.Nes, ChannelKind.Triangle)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Pulse)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Wave)]
        [InlineData(ChipKind.Snes, ChannelKind.Sample)]
        public void Quantize_ReferenceNoteHasAtMostTwoPercentError(ChipKind chip, ChannelKind channel)
        {
            double expected = SignalAnalysis.ExpectedFrequency(SynthSamples.ReferenceNote);
            double actual = PitchTable.Quantize(chip, channel, SynthSamples.ReferenceNote);

            Assert.InRange(Math.Abs(actual / expected - 1), 0, SignalAnalysis.FrequencyRelativeTolerance);
        }

        /// <summary>MIDI 範囲の両端を正の有限周波数へ補正し、音程順序を維持する。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, ChannelKind.Pulse)]
        [InlineData(ChipKind.Nes, ChannelKind.Triangle)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Pulse)]
        [InlineData(ChipKind.GameBoy, ChannelKind.Wave)]
        [InlineData(ChipKind.Snes, ChannelKind.Sample)]
        public void ClampMidiNote_EndpointFrequenciesAreFiniteAndOrdered(ChipKind chip, ChannelKind channel)
        {
            double lowerNote = PitchTable.ClampMidiNote(chip, channel, MinimumMidiNote);
            double upperNote = PitchTable.ClampMidiNote(chip, channel, MaximumMidiNote);
            double lowerFrequency = PitchTable.Quantize(chip, channel, MinimumMidiNote);
            double upperFrequency = PitchTable.Quantize(chip, channel, MaximumMidiNote);

            Assert.False(double.IsNaN(lowerNote) || double.IsInfinity(lowerNote));
            Assert.False(double.IsNaN(upperNote) || double.IsInfinity(upperNote));
            Assert.InRange(lowerFrequency, NumericalTolerance, double.MaxValue);
            Assert.InRange(upperFrequency, lowerFrequency + NumericalTolerance, double.MaxValue);
            Assert.True(lowerNote <= SynthSamples.ReferenceNote + NumericalTolerance);
            Assert.True(upperNote + NumericalTolerance >= SynthSamples.ReferenceNote);
            Assert.Equal(lowerNote, PitchTable.ClampMidiNote(chip, channel, lowerNote), precision: 9);
            Assert.Equal(upperNote, PitchTable.ClampMidiNote(chip, channel, upperNote), precision: 9);
        }

        /// <summary>SNES は一倍を 4096 とする 14 bit ピッチレジスタに量子化する。</summary>
        [Fact]
        public void Quantize_SnesUsesFourteenBitPitchRatio()
        {
            const double RootFrequency = 250;
            const int UnityRatioRegister = 4096;
            const int MaximumRatioRegister = 16383;
            const double FractionalMidiNote = 69.25;
            double rootFrequency = RootFrequency;
            double frequency = PitchTable.Quantize(ChipKind.Snes, ChannelKind.Sample, FractionalMidiNote);
            double registerValue = frequency / rootFrequency * UnityRatioRegister;
            double upperFrequency = PitchTable.Quantize(ChipKind.Snes, ChannelKind.Sample, MaximumMidiNote);
            double expectedUpperFrequency = rootFrequency * MaximumRatioRegister / UnityRatioRegister;

            Assert.InRange(Math.Abs(registerValue - Math.Round(registerValue)), 0, NumericalTolerance);
            Assert.InRange(registerValue, 1, MaximumRatioRegister);
            Assert.InRange(Math.Abs(upperFrequency - expectedUpperFrequency), 0, NumericalTolerance);
        }

        /// <summary>巨大なアルペジオが整数オーバーフローして下限音へ反転しない。</summary>
        [Fact]
        public void NoteOn_ExtremeArpeggioClampsAtUpperPitch()
        {
            var extremeInstrument = new NesPulseInstrument
            {
                ArpeggioMacro = new Macro { Values = new int[] { int.MaxValue }, LoopIndex = -1 }
            };
            float[] extreme = SynthSamples.Render(new NesPulseSynthesizer(), extremeInstrument);
            float[] upperBoundary = SynthSamples.Render(new NesPulseSynthesizer(), new NesPulseInstrument(), midiNote: MaximumMidiNote);

            Assert.Equal(upperBoundary, extreme);
            Assert.True(SignalAnalysis.RootMeanSquare(extreme) > SignalAnalysis.SilenceTolerance);
        }
    }
}
