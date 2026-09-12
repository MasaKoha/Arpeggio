using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Formats.Export.Control
{
    /// <summary>診断と後続変換で参照する、展開前の不変なノート情報。</summary>
    public sealed class ControlNote
    {
        internal ControlNote(Note note, int sourceEvent)
        {
            SourceEvent = sourceEvent;
            Tick = note.Tick;
            DurationTicks = note.DurationTicks;
            MidiNote = note.MidiNote;
            Volume = note.Volume;
            InstrumentId = note.InstrumentId;
            Effects = Array.AsReadOnly((NoteEffect[])note.Effects.Clone());
        }

        /// <summary>元トラック内のノート番号。</summary>
        public int SourceEvent { get; }
        /// <summary>展開前の開始 tick。</summary>
        public int Tick { get; }
        /// <summary>Delay を含む元ノート長。</summary>
        public int DurationTicks { get; }
        /// <summary>変調前の MIDI ノート番号。</summary>
        public int MidiNote { get; }
        /// <summary>変調前の 0〜15 の音量。</summary>
        public int Volume { get; }
        /// <summary>音色識別子。</summary>
        public int InstrumentId { get; }
        /// <summary>元ノートに指定された効果の不変コピー。</summary>
        public IReadOnlyList<NoteEffect> Effects { get; }
    }
}
