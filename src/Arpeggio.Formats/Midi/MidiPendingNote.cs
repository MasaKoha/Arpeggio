using System;

namespace Arpeggio.Formats.Midi
{
    /// <summary>FIFO と pedal 保持で共有し、収集完了後に不変ノートへ固定する発音状態。</summary>
    internal sealed class MidiPendingNote
    {
        private const int MaximumMidiValue = 127;
        private const int MaximumNoteVolume = 15;

        internal MidiPendingNote(MidiEvent source, MidiChannelState channel)
        {
            Source = source;
            Program = channel.Program;
            ChannelVolume = channel.Volume;
            Expression = channel.Expression;
            // 同じ整数積の発音を、浮動小数点の演算順によって声割り当て時に順位付けしない。
            int volumeProduct = source.DataTwo * ChannelVolume * Expression;
            const int MaximumVolumeProduct = MaximumMidiValue * MaximumMidiValue * MaximumMidiValue;
            EffectiveVolume = volumeProduct / (double)MaximumVolumeProduct;
            Volume = EffectiveVolume == 0 ? 0 : Math.Max(1, (int)Math.Round(MaximumNoteVolume * EffectiveVolume, MidpointRounding.AwayFromZero));
        }

        internal MidiEvent Source { get; }
        internal int Program { get; }
        internal int ChannelVolume { get; }
        internal int Expression { get; }
        internal double EffectiveVolume { get; }
        internal int Volume { get; }
        internal long? KeyOffTick { get; set; }
        internal long? EndTick { get; set; }
        internal long? SoundOffTick { get; set; }
    }
}
