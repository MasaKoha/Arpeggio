using System;
using Arpeggio.Core.Document;
using Arpeggio.Daw.Themes;
using Avalonia.Media;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>UI を起動せずチャンネルの色・記号・入力境界を検証する。</summary>
    public sealed class ChannelPaletteTests
    {
        /// <summary>全有効種別に決定済みの色と先頭記号が割り当てられる。</summary>
        [Theory]
        [InlineData(ChannelKind.Pulse, "#FF5DF2A4", "P1")]
        [InlineData(ChannelKind.Triangle, "#FFFFB347", "TRI")]
        [InlineData(ChannelKind.Noise, "#FFFF6FD8", "NOI")]
        [InlineData(ChannelKind.Dpcm, "#FF9AA5B1", "DPCM")]
        [InlineData(ChannelKind.Wave, "#FF4FD1FF", "WAV")]
        [InlineData(ChannelKind.Sample, "#FFB892FF", "S1")]
        public void KnownChannelsReturnSpecifiedColorAndLabel(ChannelKind channel, string expectedColor, string expectedLabel)
        {
            ISolidColorBrush brush = Assert.IsAssignableFrom<ISolidColorBrush>(ChannelPalette.GetBrush(channel));
            Assert.Equal(Color.Parse(expectedColor), brush.Color);
            Assert.Equal(1, brush.Opacity);
            Assert.Equal(expectedLabel, ChannelPalette.GetShortLabel(channel, 0));
            Assert.Same(brush, ChannelPalette.GetBrush(channel));
        }

        /// <summary>enum に種別が追加されても表示漏れを検出する。</summary>
        [Fact]
        public void EveryDefinedChannelHasColorAndSymbol()
        {
            foreach (ChannelKind channel in Enum.GetValues<ChannelKind>())
            {
                if (channel == ChannelKind.None) { continue; }
                Assert.NotNull(ChannelPalette.GetBrush(channel));
                Assert.False(string.IsNullOrWhiteSpace(ChannelPalette.GetShortLabel(channel, 0)));
            }
        }

        /// <summary>同種内の 0 始まり番号を人間向けの記号へ変換する。</summary>
        [Theory]
        [InlineData(ChannelKind.Pulse, 1, "P2")]
        [InlineData(ChannelKind.Sample, 0, "S1")]
        [InlineData(ChannelKind.Sample, 1, "S2")]
        [InlineData(ChannelKind.Sample, 2, "S3")]
        [InlineData(ChannelKind.Sample, 3, "S4")]
        [InlineData(ChannelKind.Sample, 4, "S5")]
        [InlineData(ChannelKind.Sample, 5, "S6")]
        [InlineData(ChannelKind.Sample, 6, "S7")]
        [InlineData(ChannelKind.Sample, 7, "S8")]
        public void ChannelIndicesProduceDistinctSymbols(ChannelKind channel, int channelIndex, string expected)
        {
            Assert.Equal(expected, ChannelPalette.GetShortLabel(channel, channelIndex));
        }

        /// <summary>未指定・未定義の種別は代替色に隠さず拒否する。</summary>
        [Theory]
        [InlineData(ChannelKind.None)]
        [InlineData((ChannelKind)(-1))]
        [InlineData((ChannelKind)int.MaxValue)]
        public void InvalidKindsThrow(ChannelKind channel)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChannelPalette.GetBrush(channel));
            Assert.Throws<ArgumentOutOfRangeException>(() => ChannelPalette.GetShortLabel(channel, 0));
        }

        /// <summary>重複記号や配列境界例外になる番号を拒否する。</summary>
        [Theory]
        [InlineData(ChannelKind.Pulse, -1)]
        [InlineData(ChannelKind.Pulse, 2)]
        [InlineData(ChannelKind.Sample, -1)]
        [InlineData(ChannelKind.Sample, 8)]
        [InlineData(ChannelKind.Triangle, 1)]
        [InlineData(ChannelKind.Noise, 1)]
        [InlineData(ChannelKind.Dpcm, 1)]
        [InlineData(ChannelKind.Wave, 1)]
        public void InvalidChannelIndicesThrow(ChannelKind channel, int channelIndex)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChannelPalette.GetShortLabel(channel, channelIndex));
        }

        /// <summary>Pulse 2 だけ別色になり、他の種別は番号で色が変わらない。</summary>
        [Fact]
        public void SecondPulseHasDistinctColorWhileOthersShareKindColor()
        {
            Assert.NotEqual(ChannelPalette.GetBrush(ChannelKind.Pulse, 0), ChannelPalette.GetBrush(ChannelKind.Pulse, 1));
            Assert.Same(ChannelPalette.GetBrush(ChannelKind.Pulse), ChannelPalette.GetBrush(ChannelKind.Pulse, 0));
            Assert.Same(ChannelPalette.GetBrush(ChannelKind.Sample), ChannelPalette.GetBrush(ChannelKind.Sample, 7));
        }
    }
}
