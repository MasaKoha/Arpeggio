using System;
using System.Linq;
using System.Text.Json;
using Arpeggio.Core.Document;

namespace Arpeggio.Core.Session
{
    /// <summary>CLI と MCP の情報表示を同じ JSON 形式へ揃える。</summary>
    public static class SessionOutput
    {
        /// <summary>camelCase・文字列 enum・インデント付きで出力する。</summary>
        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, InstrumentJson.CreateOptions());
        }

        /// <summary>曲の概要・音色・トラック情報と履歴件数を返す。</summary>
        public static object Info(EditSession session)
        {
            Song song = session.GetSong();
            return new
            {
                path = session.Path, song.Title, song.Chip, song.TempoBpm, song.TicksPerBeat,
                song.LengthTicks, song.LoopStartTick, song.Instruments, song.SnesEcho,
                tracks = song.Tracks.Select((track, index) => new
                {
                    index, track.Channel, track.ChannelIndex, track.Name, track.Muted, track.Pan,
                    noteCount = track.Notes.Count
                }).ToArray(),
                undoCount = session.History.UndoCount, redoCount = session.History.RedoCount
            };
        }

        /// <summary>表示範囲と交差するノートを元の tick のまま返す。</summary>
        public static object Show(EditSession session, int? track = null, int fromTick = 0, int? toTick = null)
        {
            Song song = session.GetSong();
            string text = SongTextRenderer.Render(song, track, fromTick, toTick);
            int endTick = toTick ?? song.LengthTicks;
            return new
            {
                fromTick, toTick = endTick, ticksPerRow = Song.FixedTicksPerBeat / 4, text,
                tracks = song.Tracks.Select((value, index) => new { value, index })
                    .Where(item => !track.HasValue || item.index == track.Value)
                    .Select(item => new
                    {
                        track = item.index, item.value.Name, item.value.Channel, item.value.Muted, item.value.Pan,
                        notes = item.value.Notes.Where(note => note.Tick < endTick && (long)note.Tick + note.DurationTicks > fromTick).ToArray()
                    }).ToArray()
            };
        }
    }
}
