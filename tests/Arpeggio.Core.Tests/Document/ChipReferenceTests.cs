using System;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Document
{
    /// <summary>AI に返すチップ説明の対応範囲と入力名を検証する。</summary>
    public sealed class ChipReferenceTests
    {
        /// <summary>CLI と MCP が同じ規則でチップ名を解釈する。</summary>
        [Theory]
        [InlineData("nes", ChipKind.Nes)]
        [InlineData(" NES ", ChipKind.Nes)]
        [InlineData("GameBoy", ChipKind.GameBoy)]
        [InlineData("snes", ChipKind.Snes)]
        public void ParsesSupportedChipNames(string text, ChipKind expected)
        {
            Assert.Equal(expected, ChipReference.ParseChip(text));
        }

        /// <summary>全チップの説明に共通の時間単位と全効果が含まれる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes, "NesPulse", "NesTriangle", "NesNoise", "NesDpcm")]
        [InlineData(ChipKind.GameBoy, "GbPulse", "GbWave", "GbNoise", "envelopeStepFrames")]
        [InlineData(ChipKind.Snes, "SnesSample", "attackSeconds", "sustainLevel", "delayMilliseconds")]
        public void DescribesEveryInstrumentAndCommonEffect(ChipKind chip, string first, string second, string third, string fourth)
        {
            string reference = ChipReference.Get(chip);
            Assert.Contains(first, reference);
            Assert.Contains(second, reference);
            Assert.Contains(third, reference);
            Assert.Contains(fourth, reference);
            Assert.Contains("48 tick", reference);
            Assert.Contains("12 tick", reference);
            Assert.Contains("1/60 秒", reference);
            Assert.Contains("loopIndex", reference);
            Assert.Contains("PitchSlide", reference);
            Assert.Contains("Vibrato", reference);
            Assert.Contains("VolumeSlide", reference);
            Assert.Contains("Arpeggio", reference);
            Assert.Contains("Delay", reference);
            Assert.Contains("MIDI 60=C4", reference);
        }

        /// <summary>未指定値や数値のチップ指定を明確な引数エラーにする。</summary>
        [Fact]
        public void RejectsUnsupportedChips()
        {
            Assert.Throws<ArgumentException>(() => ChipReference.ParseChip("1"));
            Assert.Throws<ArgumentException>(() => ChipReference.ParseChip("None"));
            Assert.Throws<ArgumentException>(() => ChipReference.Get(ChipKind.None));
            Assert.Throws<ArgumentException>(() => ChipReference.Get((ChipKind)99));
        }
    }
}
