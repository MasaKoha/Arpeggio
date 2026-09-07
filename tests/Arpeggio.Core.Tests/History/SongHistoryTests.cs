using System;
using Arpeggio.Core.Document;
using Arpeggio.Core.History;
using Xunit;

namespace Arpeggio.Core.Tests.History
{
    /// <summary>有限の JSON スナップショット履歴を検証する。</summary>
    public sealed class SongHistoryTests
    {
        /// <summary>undo と redo が互いの状態を復元する。</summary>
        [Fact]
        public void UndoRedoRestoresIndependentSnapshots()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            var history = new SongHistory();
            history.Record(song);
            song.Title = "after";
            Song restored = history.Undo(song);
            Assert.Equal(string.Empty, restored.Title);
            restored.Title = "modified returned copy";
            Song redone = history.Redo(restored);
            Assert.Equal("after", redone.Title);
            Assert.Equal(1, history.UndoCount);
            Assert.Equal(0, history.RedoCount);
        }

        /// <summary>上限超過時は古い操作から捨て、ちょうど 50 件を保持する。</summary>
        [Fact]
        public void CapacityDropsOldestSnapshots()
        {
            var history = new SongHistory();
            Song song = SongFactory.Create(ChipKind.Nes);
            const int ExcessCount = 5;
            for (int index = 0; index < SongHistory.Capacity + ExcessCount; index++)
            {
                song.Title = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                history.Record(song);
            }
            Assert.Equal(SongHistory.Capacity, history.UndoCount);
            for (int index = 0; index < SongHistory.Capacity; index++)
            {
                song = history.Undo(song);
            }
            Assert.Equal(ExcessCount.ToString(System.Globalization.CultureInfo.InvariantCulture), song.Title);
            Assert.Equal(0, history.UndoCount);
            Assert.Equal(SongHistory.Capacity, history.RedoCount);
            Assert.Throws<InvalidOperationException>(() => history.Undo(song));
        }

        /// <summary>undo 後の新しい操作は redo を破棄し、Clear は全履歴を消す。</summary>
        [Fact]
        public void NewEditClearsRedoAndClearRemovesAllHistory()
        {
            var history = new SongHistory();
            Song song = SongFactory.Create(ChipKind.Nes);
            history.Record(song);
            song.Title = "second";
            song = history.Undo(song);
            history.Record(song);
            Assert.Equal(0, history.RedoCount);
            history.Clear();
            Assert.Equal(0, history.UndoCount);
            Assert.Equal(0, history.RedoCount);
        }
    }
}
