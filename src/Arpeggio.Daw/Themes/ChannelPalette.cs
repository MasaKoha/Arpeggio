using System;
using Arpeggio.Core.Document;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Arpeggio.Daw.Themes
{
    /// <summary>UI の起動状態に依存しない、チャンネル色と識別記号の規約。</summary>
    public static class ChannelPalette
    {
        private const int PulseChannelCount = 2;
        private const int SampleChannelCount = 8;
        private static readonly IBrush pulse = new ImmutableSolidColorBrush(0xFF5DF2A4);
        // Pulse 2 は同じ緑だと Pulse 1 のメロディと和音のゴーストが混ざって読めないため、青緑へずらす
        private static readonly IBrush secondPulse = new ImmutableSolidColorBrush(0xFF3EE0C8);
        private static readonly IBrush triangle = new ImmutableSolidColorBrush(0xFFFFB347);
        private static readonly IBrush noise = new ImmutableSolidColorBrush(0xFFFF6FD8);
        private static readonly IBrush dpcm = new ImmutableSolidColorBrush(0xFF9AA5B1);
        private static readonly IBrush wave = new ImmutableSolidColorBrush(0xFF4FD1FF);
        private static readonly IBrush sample = new ImmutableSolidColorBrush(0xFFB892FF);
        private static readonly string[] pulseLabels = { "P1", "P2" };
        private static readonly string[] sampleLabels = { "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8" };

        /// <summary>チャンネル種別の共有不変ブラシを返す。未指定・未定義の種別は拒否する。</summary>
        public static IBrush GetBrush(ChannelKind channel) => channel switch
        {
            ChannelKind.Pulse => pulse,
            ChannelKind.Triangle => triangle,
            ChannelKind.Noise => noise,
            ChannelKind.Dpcm => dpcm,
            ChannelKind.Wave => wave,
            ChannelKind.Sample => sample,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "表示可能なチャンネル種別が必要です。")
        };

        /// <summary>同種内の番号も考慮した表示色。Pulse 2 だけ青緑へずらし、他は種別の色をそのまま返す。</summary>
        public static IBrush GetBrush(ChannelKind channel, int channelIndex)
        {
            if (channel == ChannelKind.Pulse && channelIndex == 1)
            {
                return secondPulse;
            }
            return GetBrush(channel);
        }

        /// <summary>同種内の 0 始まり番号から識別記号を返す。存在しない番号は拒否する。</summary>
        public static string GetShortLabel(ChannelKind channel, int channelIndex) => (channel, channelIndex) switch
        {
            (ChannelKind.Pulse, >= 0 and < PulseChannelCount) => pulseLabels[channelIndex],
            (ChannelKind.Triangle, 0) => "TRI",
            (ChannelKind.Noise, 0) => "NOI",
            (ChannelKind.Dpcm, 0) => "DPCM",
            (ChannelKind.Wave, 0) => "WAV",
            (ChannelKind.Sample, >= 0 and < SampleChannelCount) => sampleLabels[channelIndex],
            (ChannelKind.None, _) => throw new ArgumentOutOfRangeException(nameof(channel), channel, "未指定のチャンネルは表示できません。"),
            _ => RejectInvalidChannel(channel, channelIndex)
        };

        private static string RejectInvalidChannel(ChannelKind channel, int channelIndex)
        {
            GetBrush(channel);
            throw new ArgumentOutOfRangeException(nameof(channelIndex), channelIndex, "チャンネル番号が範囲外です。");
        }
    }
}
