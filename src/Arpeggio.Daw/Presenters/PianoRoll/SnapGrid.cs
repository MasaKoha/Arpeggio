using System;
using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Presenters.PianoRoll
{
    /// <summary>固定 PPQ に対するグリッド単位と丸めを定義する。</summary>
    public static class SnapGrid
    {
        /// <summary>選択単位の tick 数。スナップなしは最小編集単位を返す。</summary>
        public static int ToTicks(SnapResolution resolution) => resolution switch
        {
            SnapResolution.None => 1,
            SnapResolution.Bar => Song.FixedTicksPerBeat * 4,
            SnapResolution.Half => Song.FixedTicksPerBeat * 2,
            SnapResolution.Quarter => Song.FixedTicksPerBeat,
            SnapResolution.Eighth => Song.FixedTicksPerBeat / 2,
            SnapResolution.Sixteenth => Song.FixedTicksPerBeat / 4,
            SnapResolution.Triplet => Song.FixedTicksPerBeat / 3,
            _ => throw new ArgumentOutOfRangeException(nameof(resolution))
        };
        /// <summary>Alt 押下中だけ選択単位を一時解除する。</summary>
        public static int Snap(double tick, SnapResolution resolution, bool bypassSnap)
        {
            int unit = bypassSnap ? 1 : ToTicks(resolution);
            return checked((int)Math.Round(tick / unit, MidpointRounding.AwayFromZero) * unit);
        }
    }
}
