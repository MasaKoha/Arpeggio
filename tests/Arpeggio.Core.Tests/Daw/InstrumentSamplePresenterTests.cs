using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Instruments;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>SNES サンプル取り込みの履歴と既存音色入力の保持を検証する。</summary>
    public sealed class InstrumentSamplePresenterTests
    {
        private const int SampleRate = 22050;

        /// <summary>WAV の元レート・ルート音・ループを一履歴で取り込み、通常の音色編集後も維持する。</summary>
        [Fact]
        public void ImportRecordsOneHistoryAndPreservesSampleThroughParameterEdit()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            OpenSnes(fixture);
            string wavPath = Path.ChangeExtension(fixture.Path, ".wav");
            WavWriter.Write(wavPath, new float[] { 0.5f, 0.25f, -0.5f, -0.25f, 0, 0 }, SampleRate);
            SnesSampleInstrument original = Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument);
            int history = fixture.Document.Session.History.UndoCount;

            fixture.Presenter.Instruments.ImportWav(wavPath, "D4", false);

            SnesSampleInstrument imported = Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument);
            Assert.NotSame(original, imported);
            Assert.Null(original.SampleData);
            Assert.Equal(history + 1, fixture.Document.Session.History.UndoCount);
            Assert.Equal(3, imported.SampleCount);
            Assert.Equal(SampleRate, imported.SampleRate);
            Assert.Equal(62, imported.RootMidiNote);
            Assert.False(imported.Loop);
            Assert.Equal("sample 3 smp @ 22050 Hz", imported.SampleSummary);
            Assert.DoesNotContain(fixture.Presenter.Instruments.GetParameters(), parameter => parameter.Key == "sampleData");

            Dictionary<string, string> values = fixture.Presenter.Instruments.GetParameters()
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);
            fixture.Presenter.Instruments.Apply("取り込んだ音色", values);
            SnesSampleInstrument edited = Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument);
            Assert.Equal(imported.SampleData, edited.SampleData);
            Assert.Equal(imported.SampleSummary, edited.SampleSummary);
            fixture.Presenter.Undo();
            fixture.Presenter.Undo();
            Assert.Null(Assert.IsType<SnesSampleInstrument>(fixture.Presenter.Instruments.CurrentInstrument).SampleData);
        }

        /// <summary>取り込み失敗は元音色と履歴を変更しない。</summary>
        [Fact]
        public void FailedImportPreservesOriginalAndHistory()
        {
            using DawPresenterFixture fixture = new DawPresenterFixture();
            OpenSnes(fixture);
            string wavPath = Path.ChangeExtension(fixture.Path, ".wav");
            File.WriteAllText(wavPath, "invalid WAV");
            string before = SongSerializer.Serialize(fixture.Document.Song);
            int history = fixture.Document.Session.History.UndoCount;
            fixture.Presenter.Execute(() => fixture.Presenter.Instruments.ImportWav(wavPath, "C4", true));
            Assert.Equal(before, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(history, fixture.Document.Session.History.UndoCount);
            Assert.NotEmpty(fixture.View.Status);
        }

        private static void OpenSnes(DawPresenterFixture fixture)
        {
            string path = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "snes.arpeggio.json");
            SfxPresetFile.Create(path, ChipKind.Snes, SfxPresetKind.Jump);
            fixture.Presenter.Open(path);
        }
    }
}
