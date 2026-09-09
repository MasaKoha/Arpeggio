using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Import;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp
{
    /// <summary>既存の音色 JSON 更新ツールに optional マクロを通す。</summary>
    public sealed class OptionalInstrumentMacroToolsTests
    {
        /// <summary>GB デューティは JSON 全体置換・保存・再オープンで保持する。</summary>
        [Fact]
        public void GameBoyDutySurvivesJsonReplacementAndReopen()
        {
            using var files = new SampleFileFixture();
            var session = new EditSession();
            var tools = new ArpeggioTools(session);
            RequireSuccess(tools.NewSong(files.SongPath, "gameboy"));
            RequireSuccess(tools.UpdateInstrument("{\"kind\":\"GbPulse\",\"id\":1,\"dutyMacro\":{\"values\":[1,2,3,4],\"loopIndex\":1}}"));
            RequireSuccess(tools.OpenSong(files.SongPath));
            Song song = Assert.IsType<Song>(session.Song);
            var instrument = Assert.IsType<GbPulseInstrument>(song.Instruments[0]);
            Assert.NotNull(instrument.DutyMacro);
            Assert.Equal(new[] { 1, 2, 3, 4 }, instrument.DutyMacro.Values);
            Assert.Equal(1, instrument.DutyMacro.LoopIndex);
        }

        /// <summary>SNES 音量は JSON 全体置換・保存・再オープンで保持する。</summary>
        [Fact]
        public void SnesVolumeSurvivesJsonReplacementAndReopen()
        {
            using var files = new SampleFileFixture();
            var session = new EditSession();
            var tools = new ArpeggioTools(session);
            RequireSuccess(tools.NewSong(files.SongPath, "snes"));
            RequireSuccess(tools.UpdateInstrument("{\"kind\":\"SnesSample\",\"id\":1,\"volumeMacro\":{\"values\":[12,8,4,0],\"loopIndex\":1}}"));
            RequireSuccess(tools.OpenSong(files.SongPath));
            Song song = Assert.IsType<Song>(session.Song);
            var instrument = Assert.IsType<SnesSampleInstrument>(song.Instruments[0]);
            Assert.NotNull(instrument.VolumeMacro);
            Assert.Equal(new[] { 12, 8, 4, 0 }, instrument.VolumeMacro.Values);
            Assert.Equal(1, instrument.VolumeMacro.LoopIndex);
        }

        private static void RequireSuccess(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("error", out _), json);
        }
    }
}
