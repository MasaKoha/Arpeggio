using System;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Document
{
    /// <summary>共用音名変換の表記・境界・不正入力を検証する。</summary>
    public sealed class NoteNameTests
    {
        /// <summary>自然音・異名同音・数値入力を MIDI 番号へ変換する。</summary>
        [Theory]
        [InlineData("C5", 72)]
        [InlineData("C#5", 73)]
        [InlineData("Db5", 73)]
        [InlineData("60", 60)]
        [InlineData("C4", 60)]
        [InlineData(" c4 ", 60)]
        [InlineData("C-1", 0)]
        [InlineData("G9", 127)]
        [InlineData("B#3", 60)]
        [InlineData("Cb4", 59)]
        [InlineData("Fb4", 64)]
        public void ParsesNoteNames(string text, int expected)
        {
            Assert.Equal(expected, NoteName.Parse(text));
        }

        /// <summary>全 MIDI 番号の正規表記を往復できる。</summary>
        [Fact]
        public void RoundTripsEveryMidiNote()
        {
            const int MaximumMidiNote = 127;
            for (int midiNote = 0; midiNote <= MaximumMidiNote; midiNote++)
            {
                Assert.Equal(midiNote, NoteName.Parse(NoteName.Format(midiNote)));
            }
            Assert.Equal("C4", NoteName.Format(60));
            Assert.Equal("C#5", NoteName.Format(73));
            Assert.Equal("C-1", NoteName.Format(0));
        }

        /// <summary>範囲外・不完全な音名・オーバーフローを操作エラーとして拒否する。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("C")]
        [InlineData("C#")]
        [InlineData("H4")]
        [InlineData("C##4")]
        [InlineData("60.0")]
        [InlineData("-1")]
        [InlineData("128")]
        [InlineData("Cb-1")]
        [InlineData("G#9")]
        [InlineData("C2147483647")]
        [InlineData("C-2147483648")]
        [InlineData("C999999999999999999999")]
        public void RejectsInvalidNames(string text)
        {
            Assert.ThrowsAny<ArgumentException>(() => NoteName.Parse(text));
        }

        /// <summary>音名表示時も MIDI の範囲を検証する。</summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(128)]
        public void RejectsInvalidMidiForFormatting(int midiNote)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NoteName.Format(midiNote));
        }
    }
}
