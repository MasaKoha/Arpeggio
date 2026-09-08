using System.IO;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Instruments.Snes;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Import;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp
{
    /// <summary>MCP のプリセット一覧・既定編成・音色 JSON の入力経路を検証する。</summary>
    public sealed class SnesBankToolsTests
    {
        /// <summary>セッション未作成でも全音色の推奨値を列挙する。</summary>
        [Fact]
        public void Catalog_ExposesAllPresetsWithRecommendations()
        {
            ArpeggioTools tools = new ArpeggioTools(new EditSession());
            using JsonDocument catalog = JsonDocument.Parse(tools.SnesPresets());
            const int ExpectedPresetCount = 16;
            Assert.Equal(ExpectedPresetCount, catalog.RootElement.GetArrayLength());
            foreach (JsonElement preset in catalog.RootElement.EnumerateArray())
            {
                Assert.True(SnesInstrumentCatalog.TryGet(preset.GetProperty("name").GetString()!, out _));
                Assert.True(preset.TryGetProperty("category", out _));
                Assert.True(preset.TryGetProperty("description", out _));
                Assert.True(preset.TryGetProperty("adsrRegisters", out _));
                Assert.True(preset.TryGetProperty("rootMidiNote", out _));
                Assert.True(preset.TryGetProperty("loop", out _));
                Assert.True(preset.TryGetProperty("echoSend", out _));
            }
        }

        /// <summary>新規バンクの音色省略・追加・置換・再オープンにプリセットが反映される。</summary>
        [Theory]
        [InlineData("orchestral")]
        [InlineData("band")]
        [InlineData("chip")]
        public void BankAndInstrumentJson_WorkThroughExistingTools(string bank)
        {
            using SampleFileFixture files = new SampleFileFixture();
            EditSession session = new EditSession();
            ArpeggioTools tools = new ArpeggioTools(session);
            RequireSuccess(tools.NewSong(files.SongPath, "snes", bank: bank));
            RequireSuccess(tools.AddNote(7, 0, 24, "C4"));
            Assert.Equal(8, Assert.Single(session.Song!.Tracks[7].Notes).InstrumentId);
            RequireSuccess(tools.AddInstrument("{\"kind\":\"SnesSample\",\"id\":9,\"name\":\"voice\",\"preset\":\"strings\"}"));
            Assert.Equal(SnesInstrumentCatalog.Get("strings").AdsrRegisters, Assert.IsType<SnesSampleInstrument>(session.Song!.Instruments[8]).AdsrRegisters);
            RequireSuccess(tools.UpdateInstrument("{\"kind\":\"SnesSample\",\"id\":9,\"name\":\"voice\",\"echoSend\":0.6,\"preset\":\"brass\"}"));
            RequireSuccess(tools.OpenSong(files.SongPath));
            SnesSampleInstrument instrument = Assert.IsType<SnesSampleInstrument>(session.Song!.Instruments[8]);
            Assert.Equal("brass", instrument.Preset);
            Assert.Equal(0.6, instrument.EchoSend);
            Assert.Equal(SnesInstrumentCatalog.Get("brass").AdsrRegisters, instrument.AdsrRegisters);
            Assert.Null(instrument.SampleData);
            Assert.Contains("SnesSample brass", tools.ShowSong());
        }

        /// <summary>不正入力・二重指定は保存されず、割り当て中の音色は削除できない。</summary>
        [Fact]
        public void InvalidInput_DoesNotMutateDocument()
        {
            using SampleFileFixture files = new SampleFileFixture();
            EditSession session = new EditSession();
            ArpeggioTools tools = new ArpeggioTools(session);
            RequireError(tools.NewSong(files.SongPath, "nes", bank: "band"));
            Assert.False(File.Exists(files.SongPath));
            RequireError(tools.NewSong(files.SongPath, "snes", bank: "missing"));
            RequireSuccess(tools.NewSong(files.SongPath, "snes", bank: "band"));
            string before = File.ReadAllText(files.SongPath);
            RequireError(tools.UpdateInstrument("{\"kind\":\"SnesSample\",\"id\":1,\"preset\":\"unknown\"}"));
            RequireError(tools.UpdateInstrument("{\"kind\":\"SnesSample\",\"id\":1,\"preset\":\"strings\",\"sampleData\":\"AAA=\"}"));
            RequireError(tools.RemoveInstrument(1));
            Assert.Equal(before, File.ReadAllText(files.SongPath));
        }

        private static void RequireSuccess(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("error", out _), json);
        }

        private static void RequireError(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.True(document.RootElement.TryGetProperty("error", out _), json);
        }
    }
}
