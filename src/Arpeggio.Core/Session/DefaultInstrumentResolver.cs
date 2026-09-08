using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Session
{
    /// <summary>音色 ID が省略されたノートに、トラックのチャンネルへ使える音色を割り当てる。</summary>
    public static class DefaultInstrumentResolver
    {
        /// <summary>指定 ID、トラックの既定 ID、チャンネルに合う最初の音色 ID の順に選ぶ。合う音色が無ければ追加手順を含む例外を投げる。</summary>
        public static int Resolve(Song song, int trackIndex, int? instrumentId)
        {
            if (instrumentId.HasValue)
            {
                return instrumentId.Value;
            }
            Track track = EditSession.GetTrack(song, trackIndex);
            if (track.DefaultInstrumentId is int defaultInstrumentId)
            {
                return defaultInstrumentId;
            }
            foreach (Instrument instrument in song.Instruments)
            {
                if (InstrumentValidator.GetChannel(instrument.Kind) == track.Channel)
                {
                    return instrument.Id;
                }
            }
            // AI が次に打つコマンドを推測せずに済むよう、追加すべき kind を文言に含める。
            InstrumentKind suggestedKind = SuggestKind(song.Chip, track.Channel);
            throw new ArgumentException(
                $"トラック {trackIndex}（{track.Channel}）に使える音色がありません。--instrument で ID を指定するか、instrument add --kind {suggestedKind} で追加してください。");
        }

        private static InstrumentKind SuggestKind(ChipKind chip, ChannelKind channel)
        {
            return (chip, channel) switch
            {
                (ChipKind.Nes, ChannelKind.Pulse) => InstrumentKind.NesPulse,
                (ChipKind.Nes, ChannelKind.Triangle) => InstrumentKind.NesTriangle,
                (ChipKind.Nes, ChannelKind.Noise) => InstrumentKind.NesNoise,
                (ChipKind.Nes, ChannelKind.Dpcm) => InstrumentKind.NesDpcm,
                (ChipKind.GameBoy, ChannelKind.Pulse) => InstrumentKind.GbPulse,
                (ChipKind.GameBoy, ChannelKind.Wave) => InstrumentKind.GbWave,
                (ChipKind.GameBoy, ChannelKind.Noise) => InstrumentKind.GbNoise,
                (ChipKind.Snes, ChannelKind.Sample) => InstrumentKind.SnesSample,
                _ => throw new SongValidationException("チップとチャンネルの組み合わせが不正です。")
            };
        }
    }
}
