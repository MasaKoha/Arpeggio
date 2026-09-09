using System;
using System.IO;
using System.Linq;
using Arpeggio.Cli;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Tests.Sfx;
using Xunit;

namespace Arpeggio.Core.Tests.Cli
{
    /// <summary>既存 CLI の側車履歴とロールバックに SFX 定義が含まれることを検証する。</summary>
    [Collection("Cli")]
    public sealed class SfxHistoryBoundaryTests
    {
        /// <summary>Core の一括編集履歴を側車から復元し、別呼び出しの Undo/Redo で両方の状態が戻る。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void PersistentUndoRedoRestoresDefinitionGeneratedContentAndProvenance(ChipKind chip)
        {
            using var fixture = new SfxEditFixture(chip);
            string before = SongSerializer.Serialize(fixture.Song);
            fixture.Session.Sfx.Tweak("{\"tone\":{\"baseFrequencyHz\":880},\"noise\":{\"enabled\":true}}");
            string after = SongSerializer.Serialize(fixture.Song);
            SaveHistory(fixture.Session);
            Assert.Equal(0, Invoke("undo", fixture.Path));
            Assert.Equal(before, File.ReadAllText(fixture.Path));
            Assert.Equal(0, Invoke("redo", fixture.Path));
            Assert.Equal(after, File.ReadAllText(fixture.Path));
            Assert.True(SfxSynchronization.Inspect(SongSerializer.Load(fixture.Path)).Editable);
        }

        /// <summary>未知版を detach した履歴も不透明 JSON ごと復元できる。</summary>
        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("generatorVersion")]
        [InlineData("algorithmVersion")]
        public void PersistentDetachHistoryPreservesUnknownDefinitions(string versionProperty)
        {
            using var fixture = new SfxEditFixture(initial: SfxEditFixture.CreateUnsupportedSong(versionProperty));
            string before = SongSerializer.Serialize(fixture.Song);
            fixture.Session.Sfx.Detach();
            SaveHistory(fixture.Session);
            Assert.Equal(0, Invoke("undo", fixture.Path));
            Assert.Equal(before, File.ReadAllText(fixture.Path));
            Assert.Equal(0, Invoke("redo", fixture.Path));
            Assert.Null(SongSerializer.Load(fixture.Path).Sfx);
        }

        /// <summary>sfx のみの外部変更で側車 current が不一致となり、古い履歴を適用しない。</summary>
        [Fact]
        public void DefinitionOnlyExternalChangeInvalidatesPersistentHistory()
        {
            using var fixture = new SfxEditFixture();
            fixture.Session.Sfx.Tweak("{\"tone\":{\"baseFrequencyHz\":880}}");
            SaveHistory(fixture.Session);
            byte[] history = File.ReadAllBytes(fixture.Path + ".history/state.json");
            Song external = SongSerializer.Load(fixture.Path);
            SfxDefinitionData definition = external.Sfx!.Known!;
            external.Sfx = new SfxDefinition(definition with { SourcePreset = "laser" });
            SongSerializer.Save(external, fixture.Path);
            byte[] before = File.ReadAllBytes(fixture.Path);
            Assert.Equal(1, Invoke("undo", fixture.Path));
            Assert.Equal(before, File.ReadAllBytes(fixture.Path));
            Assert.Equal(history, File.ReadAllBytes(fixture.Path + ".history/state.json"));
        }

        /// <summary>通常編集後の側車保存失敗で、sfx と改行を含む正本の全バイトを戻す。</summary>
        [Fact]
        public void HistoryWriteFailureRestoresOriginalSfxFileBytes()
        {
            using var fixture = new SfxEditFixture();
            string formatted = File.ReadAllText(fixture.Path).Replace("\n", "\r\n") + "\r\n";
            File.WriteAllText(fixture.Path, formatted);
            byte[] before = File.ReadAllBytes(fixture.Path);
            string historyPath = fixture.Path + ".history";
            File.WriteAllText(historyPath, "履歴を保存できない状態");
            string operations = System.IO.Path.Combine(fixture.DirectoryPath, "operations.json");
            File.WriteAllText(operations, "[{\"kind\":\"SetTempo\",\"tempoBpm\":180}]");
            Assert.Equal(3, Invoke("apply", fixture.Path, "--operations", operations));
            Assert.Equal(before, File.ReadAllBytes(fixture.Path));
            Assert.Equal("履歴を保存できない状態", File.ReadAllText(historyPath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
        }

        private static void SaveHistory(EditSession session)
        {
            string directory = session.Path + ".history";
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, "state.json"), SessionOutput.Serialize(new
            {
                current = SongSerializer.Serialize(session.Song!),
                undo = session.History.GetUndoSnapshots().Select(SongSerializer.Serialize).ToArray(),
                redo = session.History.GetRedoSnapshots().Select(SongSerializer.Serialize).ToArray()
            }));
        }

        private static int Invoke(params string[] arguments)
        {
            TextWriter originalOutput = Console.Out;
            TextWriter originalError = Console.Error;
            try
            {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                return CliExecution.Run(arguments);
            }
            finally
            {
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }
        }
    }
}
