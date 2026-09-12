using System.IO;
using System.Reactive.Concurrency;
using System.Text;
using System.Text.Json;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Cli;
using Arpeggio.Core.Tests.Mcp.Sfx;
using Arpeggio.Core.Tests.Sfx;
using Arpeggio.Daw.Presenters.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Daw.Presenters.Sfx
{
    /// <summary>全24プリセットの作成・入力・保存を Core／CLI／MCP／DAW の境界をまたいで検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxFrontendRegressionTests
    {
        private const string CommonPatch = "\"tone\":{\"baseFrequencyHz\":330.1234564," +
            "\"slideSemitonesPerSecond\":-12.1234564,\"envelope\":{\"decaySeconds\":0.2166667}}," +
            "\"noise\":{\"envelope\":{\"volume\":7}}";
        private const uint ExplorationSeed = 1;
        private const double MutationStrength = 0.1;
        private const string FrequencyLock = "tone.baseFrequencyHz";

        /// <summary>候補保存・Open・数値入力の一確定・正本保存・再読込で定義と生成列を失わない。</summary>
        [Theory]
        [MemberData(nameof(SfxParameterPresetCatalogTests.Cases), MemberType = typeof(SfxParameterPresetCatalogTests))]
        public void CreateTweakSaveAndReopenMatchEveryFrontend(ChipKind chip, SfxPresetKind kind)
        {
            using var files = new SfxToolFixture();
            using var workstation = new DawPresenterFixture();
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            string chipName = chip.ToString().ToLowerInvariant();
            string commandPath = files.PathFor("command.json");
            string toolPath = files.PathFor("tool.json");
            string documentPath = files.PathFor("document.json");
            Song initial = SfxEditor.CreateCandidate(preset.Parameters, chip, preset.Name, preset.Name).Song;
            string originalDocument = SongSerializer.Serialize(workstation.Document.Song);
            SfxParameterToolsTests.AssertCliEquals(files.Tools.CreateSfx(toolPath, chipName, preset.Name),
                "sfx", "create", commandPath, "--chip", chipName, "--preset", preset.Name);
            SfxToolFixture.Success(files.Tools.OpenSong(toolPath));
            SfxEditorPresenter editor = workstation.Presenter.SfxEditor;
            editor.AutoPreview = false;
            editor.NewCandidate(chip, kind);
            AssertSong(initial, editor.Model.Snapshot());
            editor.SaveNew(documentPath);
            AssertFiles(initial, commandPath, toolPath, documentPath);
            Assert.Equal(originalDocument, SongSerializer.Serialize(workstation.Document.Song));
            editor.OpenSaved();
            Assert.Equal(documentPath, workstation.Document.Path);
            Assert.False(editor.Model.IsNewCandidate);

            string revision = SfxHash.ComputeRevision(initial);
            string patch = "{" + CommonPatch + "," + ChipPatch(chip) + "}";
            string patchPath = files.PathFor("patch.json");
            File.WriteAllText(patchPath, patch);
            SfxParameterToolsTests.AssertCliEquals(files.Tools.TweakSfx(patch, revision),
                "sfx", "tweak", commandPath, "--patch", patchPath, "--expected-revision", revision);
            ApplyForm(editor, chip);
            Assert.Equal(1, editor.Model.UndoCount);
            Assert.True(workstation.Document.IsDirty);
            AssertFiles(initial, documentPath);
            Song expected = SfxEditor.CreateCandidate(
                SfxParameterPatch.Apply(preset.Parameters, chip, patch).Parameters, chip, preset.Name, preset.Name).Song;
            AssertSong(expected, workstation.Document.Song);
            AssertSong(expected, editor.Model.Snapshot());
            workstation.Presenter.Save();
            AssertFiles(expected, commandPath, toolPath, documentPath);
            Assert.False(workstation.Document.IsDirty);
            workstation.Presenter.Open(documentPath);
            AssertSong(expected, editor.Model.Snapshot());
            AssertParameters(files, commandPath, editor.Model.Snapshot());
            Assert.Equal(0, workstation.Audio.StartCount);
        }

        /// <summary>固定seedの探索・ロック付き変異と一操作Undo/Redoで、全値・出自・生成列が一致する。</summary>
        [Theory]
        [MemberData(nameof(SfxParameterPresetCatalogTests.Cases), MemberType = typeof(SfxParameterPresetCatalogTests))]
        public void RandomizeMutateAndHistoryMatchEveryFrontend(ChipKind chip, SfxPresetKind kind)
        {
            using var files = new SfxToolFixture();
            using var workstation = new DawPresenterFixture();
            SfxParameterPresetDescription preset = SfxParameterPresetCatalog.Get(kind, chip);
            string chipName = chip.ToString().ToLowerInvariant();
            string commandPath = files.PathFor("command.json");
            string toolPath = files.PathFor("tool.json");
            SfxParameterToolsTests.AssertCliEquals(files.Tools.CreateSfx(toolPath, chipName, preset.Name),
                "sfx", "create", commandPath, "--chip", chipName, "--preset", preset.Name);
            SfxToolFixture.Success(files.Tools.OpenSong(toolPath));
            SfxEditorPresenter editor = workstation.Presenter.SfxEditor;
            editor.AutoPreview = false;
            editor.NewCandidate(chip, kind);
            int historyBefore = editor.Model.UndoCount;
            string originalDocument = SongSerializer.Serialize(workstation.Document.Song);
            SfxParameterToolsTests.AssertCliEquals(files.Tools.RandomizeSfx(preset.Name, ExplorationSeed),
                "sfx", "randomize", commandPath, "--category", preset.Name, "--seed", "1");
            editor.Randomize(preset.Name, ExplorationSeed);
            Song randomized = SongSerializer.Load(commandPath);
            SfxDefinitionData randomizedDefinition = randomized.Sfx!.Known!;
            AssertSong(randomized, editor.Model.Snapshot());
            Assert.Equal(historyBefore + 1, editor.Model.UndoCount);
            SfxParameterRandomizationResult expected = SfxParameterRandomizer.Randomize(preset.Parameters, chip, preset.Name, ExplorationSeed);
            Assert.Equal(expected.Parameters, randomizedDefinition.Parameters);

            editor.SetGroupLock(FrequencyLock, true);
            SfxParameterToolsTests.AssertCliEquals(files.Tools.MutateSfx(ExplorationSeed, MutationStrength,
                    "[\"" + FrequencyLock + "\"]"),
                "sfx", "mutate", commandPath, "--seed", "1", "--strength", "0.1", "--lock", FrequencyLock);
            editor.Mutate(ExplorationSeed, MutationStrength);
            Song mutated = SongSerializer.Load(commandPath);
            SfxDefinitionData mutatedDefinition = mutated.Sfx!.Known!;
            AssertSong(mutated, editor.Model.Snapshot());
            Assert.Equal(historyBefore + 2, editor.Model.UndoCount);
            Assert.Equal(randomizedDefinition.Parameters.Tone.BaseFrequencyHz, mutatedDefinition.Parameters.Tone.BaseFrequencyHz);
            Assert.Equal(SfxHash.ComputeParametersHash(randomizedDefinition.Parameters, chip),
                mutatedDefinition.LastRandomization!.BaseParametersHash);
            AssertParameters(files, commandPath, editor.Model.Snapshot());

            InvokeHistory("undo", commandPath);
            SfxToolFixture.Success(files.Tools.Undo());
            editor.Undo();
            AssertFiles(randomized, commandPath, toolPath);
            AssertSong(randomized, editor.Model.Snapshot());
            InvokeHistory("redo", commandPath);
            SfxToolFixture.Success(files.Tools.Redo());
            editor.Redo();
            string candidatePath = files.PathFor("candidate.json");
            editor.SaveNew(candidatePath);
            AssertFiles(mutated, commandPath, toolPath, candidatePath);
            Assert.Equal(originalDocument, SongSerializer.Serialize(workstation.Document.Song));
            Assert.False(workstation.Document.IsDirty);
        }

        private static string ChipPatch(ChipKind chip) => chip switch
        {
            ChipKind.Nes => "\"nes\":{\"dutySweepPercentPerSecond\":-75}",
            ChipKind.GameBoy => "\"gameBoy\":{\"dutySweepPercentPerSecond\":-75}",
            _ => "\"snes\":{\"noiseRate\":13}"
        };

        private static void ApplyForm(SfxEditorPresenter editor, ChipKind chip)
        {
            using var form = new SfxParameterForm(editor, new HistoricalScheduler());
            form.EditText("tone.baseFrequencyHz", "330.1234564");
            form.EditText("tone.slideSemitonesPerSecond", "-12.1234564");
            form.EditText("tone.envelope.decaySeconds", "0.2166667");
            form.EditText("noise.envelope.volume", "7");
            string path = chip switch
            {
                ChipKind.Nes => "nes.dutySweepPercentPerSecond",
                ChipKind.GameBoy => "gameBoy.dutySweepPercentPerSecond",
                _ => "snes.noiseRate"
            };
            form.Drag(path, chip == ChipKind.Snes ? 13 : -75);
            Assert.True(editor.Commit(), editor.Model.Error);
        }

        private static void AssertParameters(SfxToolFixture files, string commandPath, Song candidate)
        {
            string response = files.Tools.SfxParameters(includeSchema: true);
            SfxParameterToolsTests.AssertCliEquals(response, "sfx", "params", commandPath, "--schema");
            JsonElement result = SfxToolFixture.Success(response);
            using JsonDocument saved = JsonDocument.Parse(SongSerializer.Serialize(candidate));
            JsonElement definition = saved.RootElement.GetProperty("sfx");
            Assert.True(result.GetProperty("editable").GetBoolean());
            Assert.Equal(SfxHash.ComputeRevision(candidate), result.GetProperty("revision").GetString());
            Assert.Equal(JsonSerializer.Serialize(definition.GetProperty("parameters")),
                JsonSerializer.Serialize(result.GetProperty("parameters")));
            Assert.Equal(definition.GetProperty("generatedHash").GetString(),
                result.GetProperty("generation").GetProperty("generatedHash").GetString());
        }

        private static void AssertFiles(Song expected, params string[] paths)
        {
            byte[] expectedBytes = Encoding.UTF8.GetBytes(SongSerializer.Serialize(expected));
            foreach (string path in paths)
            {
                Assert.Equal(expectedBytes, File.ReadAllBytes(path));
                AssertSong(expected, SongSerializer.Load(path));
            }
        }

        private static void AssertSong(Song expected, Song actual)
        {
            Assert.Equal(SongSerializer.Serialize(expected), SongSerializer.Serialize(actual));
            Assert.True(SfxSynchronization.Inspect(actual).Editable);
        }

        private static void InvokeHistory(string operation, string path)
        {
            var result = CliConversionFixture.Invoke(operation, path);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
        }
    }
}
