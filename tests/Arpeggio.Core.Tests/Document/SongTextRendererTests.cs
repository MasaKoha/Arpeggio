using System;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Document
{
    /// <summary>トラッカー表示の音名・時間窓・チャンネル配置を検証する。</summary>
    public sealed class SongTextRendererTests
    {
        /// <summary>開始・継続・無音を 12 tick ごとに区別し、効果を印で示す。</summary>
        [Fact]
        public void RendersStartsSustainsSilenceAndEffects()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 48);
            song.Tracks[0].Notes.Add(new Note
            {
                Tick = 0,
                DurationTicks = 24,
                MidiNote = 72,
                Effects = new[] { new NoteEffect(NoteEffectKind.PitchSlide, -4) }
            });
            song.Tracks[1].Notes.Add(new Note { Tick = 12, DurationTicks = 12, MidiNote = 73 });

            string[] rows = GetRows(SongTextRenderer.Render(song));

            Assert.Equal(5, rows.Length);
            Assert.Equal("C-5 15 01*", GetCell(rows[1], 1));
            Assert.Equal("...", GetCell(rows[2], 1));
            Assert.Equal("---", GetCell(rows[3], 1));
            Assert.Equal("C#5 15 01", GetCell(rows[2], 2));
            Assert.Equal("0012", GetCell(rows[2], 0));
            Assert.Contains("[4] Dpcm 1", rows[0]);
        }

        /// <summary>同じ 12 tick 行の途中に始まる短いノートも省略しない。</summary>
        [Fact]
        public void PreservesMultipleOffGridStartsWithinOneRow()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 24);
            song.Tracks[0].Notes.Add(new Note { Tick = 1, DurationTicks = 1, MidiNote = 60 });
            song.Tracks[0].Notes.Add(new Note { Tick = 5, DurationTicks = 2, MidiNote = 64 });
            song.Tracks[0].Notes.Add(new Note { Tick = 11, DurationTicks = 3, MidiNote = 67 });

            string[] rows = GetRows(SongTextRenderer.Render(song, trackIndex: 0));

            Assert.Equal(3, rows.Length);
            Assert.Equal("C-4 15 01 @1; E-4 15 01 @5; G-4 15 01 @11", GetCell(rows[1], 1));
            Assert.Equal("...", GetCell(rows[2], 1));
        }

        /// <summary>指定した範囲を半開区間として扱い、範囲前に始まる音は継続表示する。</summary>
        [Fact]
        public void FiltersTrackAndTickRangeWithoutInventingStarts()
        {
            Song song = SongFactory.Create(ChipKind.Nes, lengthTicks: 48);
            song.Tracks[1].Notes.Add(new Note { Tick = 0, DurationTicks = 12 });
            song.Tracks[1].Notes.Add(new Note { Tick = 12, DurationTicks = 12, MidiNote = 64 });
            song.Tracks[1].Notes.Add(new Note { Tick = 24, DurationTicks = 12, MidiNote = 67 });

            string[] rows = GetRows(SongTextRenderer.Render(song, trackIndex: 1, fromTick: 6, toTick: 24));

            Assert.Equal(3, rows.Length);
            Assert.Equal(2, rows[0].Split('|').Length);
            Assert.Contains("[1] Pulse 2", rows[0]);
            Assert.Equal("0006", GetCell(rows[1], 0));
            Assert.Equal("...; E-4 15 01 @12", GetCell(rows[1], 1));
            Assert.Equal("0018", GetCell(rows[2], 0));
            Assert.Equal("...", GetCell(rows[2], 1));
            Assert.DoesNotContain("G-4", string.Join(Environment.NewLine, rows));
        }

        /// <summary>空範囲はヘッダーのみを返し、ミュート状態を保持する。</summary>
        [Fact]
        public void ShowsMutedTrackAndEmptyRange()
        {
            Song song = SongFactory.Create(ChipKind.GameBoy);
            song.Tracks[0].Muted = true;
            string[] rows = GetRows(SongTextRenderer.Render(song, trackIndex: 0, fromTick: 12, toTick: 12));
            Assert.Single(rows);
            Assert.Contains("(muted)", rows[0]);
        }

        /// <summary>存在しないトラック・範囲を操作エラーとして拒否する。</summary>
        [Fact]
        public void RejectsInvalidRanges()
        {
            Song song = SongFactory.Create(ChipKind.Nes);
            Assert.Throws<ArgumentOutOfRangeException>(() => SongTextRenderer.Render(song, trackIndex: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SongTextRenderer.Render(song, trackIndex: 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => SongTextRenderer.Render(song, fromTick: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SongTextRenderer.Render(song, fromTick: 769));
            Assert.Throws<ArgumentOutOfRangeException>(() => SongTextRenderer.Render(song, fromTick: 24, toTick: 12));
            Assert.Throws<ArgumentOutOfRangeException>(() => SongTextRenderer.Render(song, toTick: 769));
        }

        private static string[] GetRows(string text)
        {
            return text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetCell(string row, int columnIndex)
        {
            return row.Split('|')[columnIndex].Trim();
        }
    }
}
