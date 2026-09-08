using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>同一トラック内の選択を開始 tick で保持する。</summary>
    public sealed class NoteSelection
    {
        private readonly HashSet<int> ticks = new HashSet<int>();
        /// <summary>選択件数。</summary>
        public int Count => ticks.Count;
        /// <summary>単音編集パネルが参照する先頭の選択。</summary>
        public int? FirstTick => ticks.Count == 0 ? null : ticks.Min();
        /// <summary>指定ノートが選択されているか。</summary>
        public bool Contains(int tick) => ticks.Contains(tick);
        /// <summary>全選択を解除する。</summary>
        public void Clear() => ticks.Clear();
        /// <summary>一音だけを選択する。</summary>
        public void Select(int tick) { ticks.Clear(); ticks.Add(tick); }
        /// <summary>一音の選択を反転する。</summary>
        public void Toggle(int tick)
        {
            if (!ticks.Remove(tick)) { ticks.Add(tick); }
        }
        /// <summary>編集後の開始 tick で選択を置き換える。</summary>
        public void Replace(IEnumerable<Note> notes)
        {
            ticks.Clear();
            foreach (Note note in notes) { ticks.Add(note.Tick); }
        }
        /// <summary>現在の公開ノートから選択対象を取得する。</summary>
        public Note[] Resolve(IEnumerable<Note> notes) => notes.Where(note => ticks.Contains(note.Tick)).ToArray();
    }
}
