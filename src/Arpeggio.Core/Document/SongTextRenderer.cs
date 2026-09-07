using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Arpeggio.Core.Instruments;

namespace Arpeggio.Core.Document
{
    /// <summary>ソングのノート配置をチャンネルごとのトラッカー形式へ変換する。</summary>
    public static class SongTextRenderer
    {
        private const int SixteenthNotesPerBeat = 4;
        private const int TicksPerRow = Song.FixedTicksPerBeat / SixteenthNotesPerBeat;
        private const int MinimumCellWidth = 11;
        private const string ColumnSeparator = " | ";

        /// <summary>[fromTick, toTick) を 12 tick 刻みで表示する。行途中の開始は @実tick、同じ行の複数開始はセミコロンで示す。</summary>
        public static string Render(Song song, int? trackIndex = null, int fromTick = 0, int? toTick = null)
        {
            SongValidator.Validate(song);
            int endTick = toTick ?? song.LengthTicks;
            ValidateRange(song, trackIndex, fromTick, endTick);
            int firstTrackIndex = trackIndex ?? 0;
            int trackCount = trackIndex.HasValue ? 1 : song.Tracks.Count;
            int[] notePositions = new int[trackCount];
            int[] columnWidths = new int[trackCount + 1];
            List<string[]> rows = new List<string[]>();
            string[] header = CreateHeader(song, firstTrackIndex, trackCount);
            AddRow(rows, columnWidths, header);
            for (long tick = fromTick; tick < endTick; tick += TicksPerRow)
            {
                int rowTick = (int)tick;
                int rowEndTick = (int)Math.Min(tick + TicksPerRow, endTick);
                string[] row = new string[trackCount + 1];
                row[0] = rowTick.ToString("D4", CultureInfo.InvariantCulture);
                for (int columnIndex = 0; columnIndex < trackCount; columnIndex++)
                {
                    Track track = song.Tracks[firstTrackIndex + columnIndex];
                    row[columnIndex + 1] = RenderCell(track.Notes, ref notePositions[columnIndex], rowTick, rowEndTick);
                }
                AddRow(rows, columnWidths, row);
            }
            StringBuilder output = new StringBuilder(JoinRows(rows, columnWidths));
            foreach (Instrument instrument in song.Instruments)
            {
                if (instrument is SnesSampleInstrument sample && sample.SampleData != null)
                {
                    output.AppendLine().Append($"{sample.Id:D2} {sample.Name}: {sample.SampleSummary}");
                }
            }
            return output.ToString();
        }

        private static void ValidateRange(Song song, int? trackIndex, int fromTick, int endTick)
        {
            if (trackIndex.HasValue && (trackIndex.Value < 0 || trackIndex.Value >= song.Tracks.Count))
            {
                throw new ArgumentOutOfRangeException(nameof(trackIndex), "track は 0 始まりの有効なトラック番号です。");
            }
            if (fromTick < 0 || fromTick > song.LengthTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(fromTick), "fromTick はソング内の tick です。");
            }
            if (endTick < fromTick || endTick > song.LengthTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(endTick), "toTick は fromTick 以上かつソング終端以下です。");
            }
        }

        private static string[] CreateHeader(Song song, int firstTrackIndex, int trackCount)
        {
            string[] header = new string[trackCount + 1];
            header[0] = "Tick";
            for (int columnIndex = 0; columnIndex < trackCount; columnIndex++)
            {
                int trackIndex = firstTrackIndex + columnIndex;
                Track track = song.Tracks[trackIndex];
                string mutedLabel = track.Muted ? " (muted)" : string.Empty;
                header[columnIndex + 1] = $"[{trackIndex}] {track.Name}{mutedLabel}";
            }
            return header;
        }

        private static string RenderCell(List<Note> notes, ref int notePosition, int rowTick, int rowEndTick)
        {
            while (notePosition < notes.Count && (long)notes[notePosition].Tick + notes[notePosition].DurationTicks <= rowTick)
            {
                notePosition++;
            }
            StringBuilder cell = new StringBuilder();
            int scanPosition = notePosition;
            while (scanPosition < notes.Count && notes[scanPosition].Tick < rowEndTick)
            {
                if (cell.Length > 0)
                {
                    cell.Append("; ");
                }
                Note note = notes[scanPosition];
                cell.Append(note.Tick < rowTick ? "..." : RenderNote(note, rowTick));
                scanPosition++;
            }
            return cell.Length == 0 ? "---" : cell.ToString();
        }

        private static string RenderNote(Note note, int rowTick)
        {
            string noteName = NoteName.Format(note.MidiNote);
            if (noteName[1] != '#')
            {
                noteName = noteName.Insert(1, "-");
            }
            string offset = note.Tick == rowTick ? string.Empty : " @" + note.Tick.ToString(CultureInfo.InvariantCulture);
            string effectMarker = note.Effects.Length == 0 ? string.Empty : "*";
            return noteName + " " + note.Volume.ToString("D2", CultureInfo.InvariantCulture)
                + " " + note.InstrumentId.ToString("D2", CultureInfo.InvariantCulture) + offset + effectMarker;
        }

        private static void AddRow(List<string[]> rows, int[] columnWidths, string[] row)
        {
            rows.Add(row);
            for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                int minimumWidth = columnIndex == 0 ? "Tick".Length : MinimumCellWidth;
                columnWidths[columnIndex] = Math.Max(columnWidths[columnIndex], Math.Max(minimumWidth, row[columnIndex].Length));
            }
        }

        private static string JoinRows(List<string[]> rows, int[] columnWidths)
        {
            StringBuilder output = new StringBuilder();
            foreach (string[] row in rows)
            {
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    if (columnIndex > 0)
                    {
                        output.Append(ColumnSeparator);
                    }
                    output.Append(row[columnIndex].PadRight(columnWidths[columnIndex]));
                }
                output.AppendLine();
            }
            return output.ToString();
        }
    }
}
