using System.Collections.Generic;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Formats.Midi
{
    /// <summary>採用順で生成した独立音色と、採用ノートごとの音色 ID。</summary>
    public sealed class MidiInstrumentMap
    {
        private readonly Dictionary<MidiAllocatedNote, int> _identifiers;

        internal MidiInstrumentMap(List<Instrument> instruments, Dictionary<MidiAllocatedNote, int> identifiers)
        {
            Instruments = instruments.AsReadOnly();
            _identifiers = identifiers;
        }

        /// <summary>ID 昇順の音色。呼び出しごとに独立した編集用インスタンスを所有する。</summary>
        public IReadOnlyList<Instrument> Instruments { get; }

        /// <summary>この対応表に含まれる採用ノートの音色 ID を取得する。</summary>
        public int GetInstrumentId(MidiAllocatedNote note) => _identifiers[note];
    }
}
