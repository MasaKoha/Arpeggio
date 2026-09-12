using System.IO;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Sfx.Storage;
using Arpeggio.Daw.Presenters.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>未保存候補の出力が現在文書・モニター音量へ依存しないことを守る。</summary>
    public sealed class SfxOutputPresenterTests
    {
        /// <summary>WAVはCoreのtail0と全バイト一致し、候補・現在文書を保存しない。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public async Task CandidateExportMatchesCoreAndKeepsDocumentUnchanged(ChipKind chip)
        {
            using var fixture = new DawPresenterFixture();
            SfxEditorPresenter editor = fixture.Presenter.SfxEditor;
            editor.AutoPreview = false;
            editor.NewCandidate(chip, SfxPresetKind.Hit);
            fixture.Presenter.SfxPreview.Volume = 0;
            string original = SongSerializer.Serialize(fixture.Document.Song);
            byte[] originalFile = File.ReadAllBytes(fixture.Path);
            Song snapshot = editor.Model.Snapshot();
            string directory = Path.GetDirectoryName(fixture.Path)!;
            string path = Path.Combine(directory, "candidate.wav");
            using var output = new SfxOutputPresenter();
            await output.ExportAsync(snapshot, path);
            var renderer = new SongRenderer(snapshot, new RenderSettings(44100, 1, 0));
            using var expected = new MemoryStream();
            WavWriter.Write(expected, renderer.RenderAll(), 44100);
            Assert.Equal(expected.ToArray(), File.ReadAllBytes(path));
            Assert.Equal(SfxHash.ComputeRevision(snapshot), output.Revision);
            Assert.Equal(original, SongSerializer.Serialize(fixture.Document.Song));
            Assert.Equal(originalFile, File.ReadAllBytes(fixture.Path));
            Assert.Null(editor.CandidateFile.SavedPath);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            byte[] exported = File.ReadAllBytes(path);
            await output.ExportAsync(snapshot, path);
            Assert.Contains("処理失敗", output.Result);
            Assert.Equal(exported, File.ReadAllBytes(path));
        }

        /// <summary>解析結果は開始時のrevisionを持ち、次の編集で古い結果と判別できる。</summary>
        [Fact]
        public async Task AnalysisRevisionDoesNotFollowLaterEdits()
        {
            using var fixture = new DawPresenterFixture();
            SfxEditorPresenter editor = fixture.Presenter.SfxEditor;
            editor.AutoPreview = false;
            editor.NewCandidate(ChipKind.Nes, SfxPresetKind.Jump);
            using var output = new SfxOutputPresenter();
            Song snapshot = editor.Model.Snapshot();
            await output.AnalyzeAsync(snapshot);
            Assert.Equal(SfxHash.ComputeRevision(snapshot), output.Revision);
            Assert.DoesNotContain("処理失敗", output.Result);
            editor.UpdatePatch("{\"tone\":{\"baseFrequencyHz\":880}}");
            editor.Commit();
            Assert.NotEqual(SfxHash.ComputeRevision(editor.Model.Snapshot()), output.Revision);
        }
    }
}
