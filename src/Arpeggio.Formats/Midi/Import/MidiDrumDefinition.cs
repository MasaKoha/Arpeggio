using System;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Formats.Midi.Import
{
    /// <summary>GM 打楽器の分類・固定ゲート・Noise 選択値の対応表。</summary>
    internal sealed class MidiDrumDefinition
    {
        private const int MicrosecondsPerMillisecond = 1000;
        private const int MillisecondsPerSecond = 1000;
        private const int MacroFramesPerSecond = 60;
        private const int MaximumVolume = 15;

        private MidiDrumDefinition(string preset, MidiDrumPriority priority, int nesSelection,
            int gameBoySelection, int gateMilliseconds, bool isFallback = false)
        {
            Preset = preset;
            Priority = priority;
            NesSelection = nesSelection;
            GameBoySelection = gameBoySelection;
            GateMilliseconds = gateMilliseconds;
            IsFallback = isFallback;
        }

        internal string Preset { get; }
        internal MidiDrumPriority Priority { get; }
        internal int NesSelection { get; }
        internal int GameBoySelection { get; }
        internal int GateMilliseconds { get; }
        internal long GateMicroseconds => (long)GateMilliseconds * MicrosecondsPerMillisecond;
        internal bool IsFallback { get; }

        internal static MidiDrumDefinition Get(int pitch)
        {
            // 数値は設計書の GM 対応表そのものであり、音階計算には使用しない。
            return pitch switch
            {
                35 or 36 => new MidiDrumDefinition("kick", MidiDrumPriority.Kick, 15, 72, 300),
                37 or 38 or 39 or 40 => new MidiDrumDefinition("snare", MidiDrumPriority.Snare, 10, 96, 150),
                42 or 44 => new MidiDrumDefinition("hat", MidiDrumPriority.Hat, 1, 120, 40),
                46 => new MidiDrumDefinition("openhat", MidiDrumPriority.OpenHat, 2, 112, 200),
                41 or 43 or 45 or 47 or 48 or 50 => new MidiDrumDefinition("tom", MidiDrumPriority.Tom, 12, 88, 200),
                49 or 51 or 52 or 53 or 55 or 57 or 59 => new MidiDrumDefinition("crash", MidiDrumPriority.Crash, 3, 104, 800),
                _ => new MidiDrumDefinition("snare", MidiDrumPriority.Snare, 10, 96, 150, true)
            };
        }

        internal Macro CreateVolumeMacro()
        {
            int frames = (int)Math.Ceiling((double)GateMilliseconds * MacroFramesPerSecond / MillisecondsPerSecond);
            var values = new int[frames + 1];
            for (int frame = 0; frame <= frames; frame++)
            {
                values[frame] = (int)Math.Round((double)MaximumVolume * (frames - frame) / frames, MidpointRounding.AwayFromZero);
            }
            return new Macro { Values = values, LoopIndex = -1 };
        }
    }
}
