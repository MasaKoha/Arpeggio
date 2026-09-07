using System;
using System.Collections.Generic;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.History
{
    /// <summary>正規 JSON のスナップショットを保持する有限履歴。</summary>
    public sealed class SongHistory
    {
        /// <summary>保持できる操作前スナップショット数。</summary>
        public const int Capacity = 50;
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();

        /// <summary>戻せる操作数。</summary>
        public int UndoCount => _undo.Count;
        /// <summary>やり直せる操作数。</summary>
        public int RedoCount => _redo.Count;

        /// <summary>成功した操作の直前を記録し、redo を破棄する。</summary>
        public void Record(Song before)
        {
            string snapshot = SongSerializer.Serialize(before);
            Push(_undo, snapshot);
            _redo.Clear();
        }

        /// <summary>現在の状態を redo へ移し、直前の状態の独立したコピーを返す。</summary>
        public Song Undo(Song current)
        {
            return Move(_undo, _redo, current);
        }

        /// <summary>現在の状態を undo へ移し、やり直す状態の独立したコピーを返す。</summary>
        public Song Redo(Song current)
        {
            return Move(_redo, _undo, current);
        }

        /// <summary>ソングの切り替え時に全履歴を消す。</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>古い順に独立した undo スナップショットを取得する。</summary>
        public Song[] GetUndoSnapshots()
        {
            return _undo.ConvertAll(SongSerializer.Deserialize).ToArray();
        }

        /// <summary>古い順に独立した redo スナップショットを取得する。</summary>
        public Song[] GetRedoSnapshots()
        {
            return _redo.ConvertAll(SongSerializer.Deserialize).ToArray();
        }

        /// <summary>永続化済み履歴を全件検証してから置き換える。</summary>
        public void Restore(IReadOnlyList<Song> undo, IReadOnlyList<Song> redo)
        {
            List<string> restoredUndo = SerializeSnapshots(undo);
            List<string> restoredRedo = SerializeSnapshots(redo);
            _undo.Clear();
            _undo.AddRange(restoredUndo);
            _redo.Clear();
            _redo.AddRange(restoredRedo);
        }

        private static List<string> SerializeSnapshots(IReadOnlyList<Song> snapshots)
        {
            if (snapshots.Count > Capacity)
            {
                throw new ArgumentException("履歴の保持上限を超えています。", nameof(snapshots));
            }
            List<string> serialized = new List<string>(snapshots.Count);
            foreach (Song snapshot in snapshots)
            {
                serialized.Add(SongSerializer.Serialize(snapshot));
            }
            return serialized;
        }

        internal Song PeekUndo()
        {
            return Peek(_undo);
        }

        internal Song PeekRedo()
        {
            return Peek(_redo);
        }

        private static Song Move(List<string> source, List<string> target, Song current)
        {
            Song restored = Peek(source);
            string snapshot = SongSerializer.Serialize(current);
            Push(target, snapshot);
            source.RemoveAt(source.Count - 1);
            return restored;
        }

        private static Song Peek(List<string> snapshots)
        {
            if (snapshots.Count == 0)
            {
                throw new InvalidOperationException("対象の編集履歴がありません。");
            }
            return SongSerializer.Deserialize(snapshots[snapshots.Count - 1]);
        }

        private static void Push(List<string> snapshots, string snapshot)
        {
            snapshots.Add(snapshot);
            if (snapshots.Count > Capacity)
            {
                snapshots.RemoveAt(0);
            }
        }
    }
}
