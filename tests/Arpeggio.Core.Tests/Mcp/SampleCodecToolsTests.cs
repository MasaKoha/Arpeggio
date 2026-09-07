using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Session;
using Arpeggio.Core.Tests.Import;
using Arpeggio.Mcp;
using Xunit;

namespace Arpeggio.Core.Tests.Mcp
{
    /// <summary>新 MCP ツールの JSON 契約・セッション履歴・書き出しを検証する。</summary>
    public sealed class SampleCodecToolsTests
    {
        /// <summary>取り込み・表示・undo/redo・OGG 出力を共有セッションへ接続する。</summary>
        [Fact]
        public void Tools_ImportSampleAndExportOgg()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { 0, short.MaxValue, 0, short.MinValue });
            EditSession session = new EditSession();
            ArpeggioTools tools = new ArpeggioTools(session);
            RequireSuccess(tools.NewSong(files.SongPath, "snes", lengthBeats: 4));
            Instrument previous = session.Song!.Instruments[0];
            JsonElement imported = RequireSuccess(tools.ImportWavSample(1, files.WavePath, "C5", 1, 3));
            Assert.Equal("sample 4 smp @ 22050 Hz", imported.GetProperty("sampleSummary").GetString());
            Assert.Equal(72, imported.GetProperty("rootMidiNote").GetInt32());
            Assert.False(imported.TryGetProperty("sampleData", out _));
            Assert.NotSame(previous, session.Song!.Instruments[0]);
            Assert.Equal(1, session.History.UndoCount);
            string sampleData = ((SnesSampleInstrument)session.Song!.Instruments[0]).SampleData!;
            string shown = tools.ShowSong();
            Assert.Contains("sample 4 smp @ 22050 Hz", shown);
            Assert.DoesNotContain(sampleData, shown);
            RequireSuccess(tools.Undo());
            Assert.Null(((SnesSampleInstrument)session.Song!.Instruments[0]).SampleData);
            RequireSuccess(tools.Redo());
            Assert.Equal(sampleData, ((SnesSampleInstrument)session.Song!.Instruments[0]).SampleData);
            RequireSuccess(tools.AddNote(0, 0, 192, "C5"));
            int undoBefore = session.History.UndoCount;
            string before = File.ReadAllText(files.SongPath);
            JsonElement exported = RequireSuccess(tools.ExportOgg(files.OggPath, loops: 2, sampleRate: 22050, tail: 0.25, quality: 0.4f));
            Assert.Equal(22050, exported.GetProperty("sampleRate").GetInt32());
            Assert.Equal(76073, exported.GetProperty("frames").GetInt32());
            Assert.Equal(2, exported.GetProperty("channels").GetInt32());
            Assert.Equal("OggS", Encoding.ASCII.GetString(File.ReadAllBytes(files.OggPath), 0, 4));
            Assert.Equal(before, File.ReadAllText(files.SongPath));
            Assert.Equal(undoBefore, session.History.UndoCount);
            RequireSuccess(tools.ImportWavSample(1, files.WavePath, loop: false));
            Assert.False(((SnesSampleInstrument)session.Song!.Instruments[0]).Loop);
        }

        /// <summary>未オープン・種類違い・不正範囲・入出力失敗をエラー JSON とし状態を保つ。</summary>
        [Fact]
        public void Tools_RejectInvalidRequestsWithoutMutation()
        {
            using SampleFileFixture files = new SampleFileFixture();
            files.WriteWave(new short[] { 0, 1 });
            EditSession session = new EditSession();
            ArpeggioTools tools = new ArpeggioTools(session);
            RequireError(tools.ImportWavSample(1, files.WavePath), 1);
            RequireError(tools.ExportOgg(files.OggPath), 1);
            RequireSuccess(tools.NewSong(files.SongPath, "snes"));
            string before = File.ReadAllText(files.SongPath);
            Instrument previous = session.Song!.Instruments[0];
            RequireError(tools.ImportWavSample(1, files.WavePath, loopStart: 2), 2);
            RequireError(tools.ImportWavSample(999, files.WavePath), 1);
            RequireError(tools.ImportWavSample(1, files.WavePath, rootNote: "bad"), 1);
            RequireError(tools.ImportWavSample(1, files.WavePath + ".missing"), 3);
            File.WriteAllText(files.WavePath, "broken WAV");
            RequireError(tools.ImportWavSample(1, files.WavePath), 1);
            RequireError(tools.ExportOgg(files.OggPath, quality: float.NaN), 1);
            RequireError(tools.ExportOgg(files.OggPath, tail: -1), 1);
            RequireError(tools.ExportOgg(Path.Combine(files.DirectoryPath, "missing", "out.ogg")), 3);
            Assert.Equal(before, File.ReadAllText(files.SongPath));
            Assert.Same(previous, session.Song!.Instruments[0]);
            Assert.Equal(0, session.History.UndoCount);
            Assert.False(File.Exists(files.OggPath));
            string nesPath = Path.Combine(files.DirectoryPath, "nes.json");
            RequireSuccess(tools.NewSong(nesPath, "nes"));
            RequireError(tools.ImportWavSample(1, files.WavePath), 1);
        }

        private static JsonElement RequireSuccess(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("error", out _), json);
            return document.RootElement.Clone();
        }

        private static void RequireError(string json, int exitCode)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal(exitCode, document.RootElement.GetProperty("exitCode").GetInt32());
        }
    }
}
