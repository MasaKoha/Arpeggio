using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Arpeggio.Core.Document;
using Arpeggio.Core.Render;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Presets;
using Arpeggio.Core.Tests.Cli;
using Arpeggio.Core.Tests.Mcp.Sfx;
using Arpeggio.Core.Tests.Sfx;
using Arpeggio.Core.Tests.Sfx.Presets;
using Arpeggio.Daw.Presenters.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>従来24雛形が新しいDAWの入口・保存を経由しても定義なしの音を保つことを検証する。</summary>
    [Collection("Cli")]
    public sealed class LegacySfxFrontendRegressionTests
    {
        private const int SampleRate = 44100;
        private const int Loops = 1;
        private const double TailSeconds = 0;
        private const string RejectedPatch = "{\"tone\":{\"baseFrequencyHz\":440}}";

        /// <summary>旧factory・旧CLI/MCP・DAW候補のJSONを比較し、正本保存と編集拒否後のPCM/WAVを保持する。</summary>
        [Theory]
        [MemberData(nameof(SfxParameterPresetCatalogTests.Cases), MemberType = typeof(SfxParameterPresetCatalogTests))]
        public void LegacyCreationAndDawSavePreserveJsonAndAudio(ChipKind chip, SfxPresetKind kind)
        {
            using var files = new SfxToolFixture();
            using var workstation = new DawPresenterFixture();
            string chipName = chip.ToString().ToLowerInvariant();
            string presetName = SfxPresetCatalog.Get(kind).Name;
            string commandPath = files.PathFor("command.json");
            string toolPath = files.PathFor("tool.json");
            string documentPath = files.PathFor("document.json");
            Song original = SfxPresetFactory.Create(chip, kind);
            byte[] originalBytes = Encoding.UTF8.GetBytes(SongSerializer.Serialize(original));
            var created = CliConversionFixture.Invoke("sfx", "new", commandPath, "--chip", chipName, "--preset", presetName);
            Assert.Equal(0, created.ExitCode);
            Assert.Empty(created.Error);
            SfxToolFixture.Success(files.Tools.NewSfx(toolPath, presetName, chipName));
            SfxEditorPresenter editor = workstation.Presenter.SfxEditor;
            editor.AutoPreview = false;
            editor.NewLegacyCandidate(chip, kind);
            Assert.Null(editor.Model.Snapshot().Sfx);
            editor.SaveNew(documentPath);
            editor.OpenSaved();
            Assert.Equal(documentPath, workstation.Document.Path);
            workstation.Presenter.Save();
            AssertLegacyFiles(originalBytes, commandPath, toolPath, documentPath);

            var rejected = CliConversionFixture.Invoke("sfx", "tweak", commandPath, "--frequency", "440", "--json");
            Assert.Equal(1, rejected.ExitCode);
            SfxToolFixture.Failure(rejected.Output, 1, "MissingDefinition");
            SfxToolFixture.Failure(files.Tools.TweakSfx(RejectedPatch), 1, "MissingDefinition");
            editor.UpdatePatch(RejectedPatch);
            Assert.False(editor.Commit());
            editor.Cancel();
            Assert.Equal(SfxEditabilityReason.MissingDefinition, editor.Model.Synchronization.Reason);
            Assert.Equal(0, editor.Model.UndoCount);
            AssertLegacyFiles(originalBytes, commandPath, toolPath, documentPath);
            Assert.Equal(originalBytes, Encoding.UTF8.GetBytes(SongSerializer.Serialize(editor.Model.Snapshot())));

            // 同じエンジン内の入口・保存回帰。旧エンジンで採取した基準PCMとの比較は別途行う。
            var settings = new RenderSettings(SampleRate, Loops, TailSeconds);
            float[] expected = new SongRenderer(original, settings).RenderAll();
            float[] actual = new SongRenderer(workstation.Document.Song, settings).RenderAll();
            Assert.Equal(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(), MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
            using var expectedWave = new MemoryStream();
            using var actualWave = new MemoryStream();
            WavWriter.Write(expectedWave, expected, SampleRate);
            WavWriter.Write(actualWave, actual, SampleRate);
            Assert.Equal(expectedWave.ToArray(), actualWave.ToArray());
            Assert.Equal(0, workstation.Audio.StartCount);
        }

        private static void AssertLegacyFiles(byte[] expected, params string[] paths)
        {
            foreach (string path in paths)
            {
                Assert.Equal(expected, File.ReadAllBytes(path));
                Song restored = SongSerializer.Load(path);
                Assert.Null(restored.Sfx);
                Assert.Equal(expected, Encoding.UTF8.GetBytes(SongSerializer.Serialize(restored)));
            }
        }
    }
}
