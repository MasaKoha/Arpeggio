using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Midi;
using Xunit;
using Arpeggio.Core.Tests.Formats.Export.Vgm;
using Arpeggio.Core.Tests.Formats.Midi.Import;
using Arpeggio.Formats.Midi.Import;

namespace Arpeggio.Core.Tests.Formats.Midi
{
    /// <summary>確定済み JSON の新規保存・dry-run・競合・所有権・失敗後のファイル保持を検証する。</summary>
    public sealed class MidiSongFileTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "arpeggio-midi-" + Guid.NewGuid().ToString("N"));

        /// <summary>各テストで独立した保存先を作る。</summary>
        public MidiSongFileTests()
        {
            Directory.CreateDirectory(_directory);
        }

        /// <summary>三チップの確定 JSON を BOM なしで保存し、読み戻しと同じ byte 数になる。</summary>
        [Theory]
        [InlineData(ChipKind.Nes)]
        [InlineData(ChipKind.GameBoy)]
        [InlineData(ChipKind.Snes)]
        public void NewFileRoundTripMatchesPreparedJson(ChipKind chip)
        {
            MidiImportResult result = CreateResult(chip);
            string path = Path.Combine(_directory, "song.arpeggio.json");
            MidiSongFileResult saved = MidiSongFile.Write(result, path);
            Assert.True(saved.Written);
            Assert.False(saved.DestinationExists);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(Encoding.UTF8.GetBytes(result.Json!), bytes);
            Assert.Equal(result.Report.OutputBytes, bytes.LongLength);
            Assert.Equal(result.Json, SongSerializer.Serialize(SongSerializer.Load(path)));
            Assert.Single(Directory.GetFiles(_directory));
            Assert.Empty(Directory.GetDirectories(_directory));
            Assert.Same(result.Report, saved.Report);
        }

        /// <summary>dry-run は既存パスでも全候補のサイズを返し、既存内容とディレクトリを変更しない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DryRunReportsDestinationExistenceWithoutWriting(bool exists)
        {
            MidiImportResult result = CreateResult();
            string path = Path.Combine(_directory, "song.arpeggio.json");
            byte[] original = { 1, 2, 3, 4 };
            if (exists)
            {
                File.WriteAllBytes(path, original);
            }
            MidiSongFileResult prepared = MidiSongFile.Write(result, path, dryRun: true);
            Assert.False(prepared.Written);
            Assert.Equal(exists, prepared.DestinationExists);
            Assert.True(prepared.Report.OutputBytes > 0);
            Assert.Equal(exists ? 1 : 0, Directory.GetFiles(_directory).Length);
            if (exists)
            {
                Assert.Equal(original, File.ReadAllBytes(path));
            }
        }

        /// <summary>strict と変換失敗は既存ファイルを保ち、一時ファイルも作らない。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RejectedConversionNeverWrites(bool strict)
        {
            byte[] input = strict ? MidiImporterTests.SimpleMidi() : MidiFileFixture.Create(MidiFileFixture.Bytes("00 FF 2F 00"));
            MidiImportResult result = MidiImporterTests.Import(input, new MidiImportOptions { Chip = ChipKind.Nes, Strict = strict });
            string path = Path.Combine(_directory, "existing.arpeggio.json");
            File.WriteAllText(path, "existing");
            MidiSongFileResult saved = MidiSongFile.Write(result, path);
            Assert.False(saved.Written);
            Assert.True(saved.DestinationExists);
            Assert.Equal("existing", File.ReadAllText(path));
            string newPath = Path.Combine(_directory, "new.arpeggio.json");
            Assert.False(MidiSongFile.Write(result, newPath).Written);
            Assert.False(File.Exists(newPath));
            Assert.Single(Directory.GetFiles(_directory));
            using var stream = new MemoryStream();
            stream.WriteByte(42);
            Assert.False(MidiSongFile.Write(result, stream));
            Assert.Equal(new byte[] { 42 }, stream.ToArray());
            Assert.True(stream.CanWrite);
        }

        /// <summary>通常保存は既存ファイルを拒否し、変換候補を別パスへ再利用できる。</summary>
        [Fact]
        public void ExistingDestinationIsNeverOverwritten()
        {
            MidiImportResult result = CreateResult();
            string path = Path.Combine(_directory, "song.arpeggio.json");
            File.WriteAllText(path, "original");
            Assert.Throws<IOException>(() => MidiSongFile.Write(result, path));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.True(result.CanWrite);
            Assert.True(MidiSongFile.Write(result, Path.Combine(_directory, "other.arpeggio.json")).Written);
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }

        /// <summary>二つの同時保存の一方だけが確定し、競合した一時ファイルが残らない。</summary>
        [Fact]
        public async Task ConcurrentNewSavesHaveExactlyOneWinner()
        {
            MidiImportResult result = CreateResult();
            string path = Path.Combine(_directory, "race.arpeggio.json");
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool>[] writers = Enumerable.Range(0, 2).Select(attempt => Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    return MidiSongFile.Write(result, path).Written;
                }
                catch (IOException)
                {
                    return false;
                }
            })).ToArray();
            start.SetResult(true);
            bool[] outcomes = await Task.WhenAll(writers);
            Assert.Single(outcomes, written => written);
            Assert.Equal(result.Json, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(_directory));
        }

        /// <summary>入力ファイル・候補の後編集を保存済み結果へ混入させない。</summary>
        [Fact]
        public void InputAndPreparedSnapshotArePreserved()
        {
            string inputPath = Path.Combine(_directory, "input.mid");
            byte[] input = MidiImporterTests.SimpleMidi();
            File.WriteAllBytes(inputPath, input);
            MidiImportResult result;
            using (var stream = File.OpenRead(inputPath))
            {
                result = MidiImporter.Import(stream, new MidiImportOptions { Chip = ChipKind.Nes, SourceName = inputPath });
            }
            string json = result.Json!;
            result.Song!.Title = "後から編集";
            result.Song.Tracks.Clear();
            Assert.Throws<ArgumentException>(() => MidiSongFile.Write(result, inputPath, sourcePath: inputPath));
            Assert.Throws<ArgumentException>(() => MidiSongFile.Write(result, inputPath, dryRun: true, sourcePath: Path.Combine(_directory, ".", "input.mid")));
            string path = Path.Combine(_directory, "song.arpeggio.json");
            Assert.True(MidiSongFile.Write(result, path, sourcePath: inputPath).Written);
            Assert.Equal(json, File.ReadAllText(path));
            Assert.Equal(input, File.ReadAllBytes(inputPath));
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }

        /// <summary>キャンセルは新規ファイルも一時ファイルも残さない。</summary>
        [Fact]
        public void CancelledSaveLeavesDirectoryUnchanged()
        {
            MidiImportResult result = CreateResult();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            string path = Path.Combine(_directory, "song.arpeggio.json");
            Assert.Throws<OperationCanceledException>(() => MidiSongFile.Write(result, path, cancellationToken: cancellation.Token));
            Assert.Empty(Directory.GetFiles(_directory));
            using var stream = new MemoryStream();
            Assert.Throws<OperationCanceledException>(() => MidiSongFile.Write(result, stream, cancellation.Token));
            Assert.Empty(stream.ToArray());
            Assert.True(stream.CanWrite);
        }

        /// <summary>保存先を開けない I/O 失敗を伝播し、既存データを保つ。</summary>
        [Fact]
        public void MissingParentFailureDoesNotLeaveTemporaryFiles()
        {
            MidiImportResult result = CreateResult();
            string path = Path.Combine(_directory, "missing", "song.arpeggio.json");
            Assert.Throws<DirectoryNotFoundException>(() => MidiSongFile.Write(result, path));
            Assert.Empty(Directory.GetFileSystemEntries(_directory));
            Assert.True(result.CanWrite);
        }

        /// <summary>シーク不要の出力は閉じず、予定 byte と一致する。</summary>
        [Fact]
        public void StreamWriteUsesPreparedBytesAndLeavesStreamOpen()
        {
            MidiImportResult result = CreateResult();
            using var stream = new VgmTestStream();
            Assert.True(MidiSongFile.Write(result, stream));
            Assert.True(stream.CanWrite);
            Assert.Equal(Encoding.UTF8.GetBytes(result.Json!), stream.GetWrittenBytes());
        }

        /// <summary>途中 I/O の部分 Stream は保持し、例外を伝播して所有権を維持する。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(100)]
        public void PartialStreamFailurePropagatesWithoutClosing(int failureAfterBytes)
        {
            MidiImportResult result = CreateResult();
            using var stream = new VgmTestStream(failureAfterBytes);
            Assert.Throws<IOException>(() => MidiSongFile.Write(result, stream));
            Assert.True(stream.CanWrite);
            Assert.Equal(Encoding.UTF8.GetBytes(result.Json!).Take(failureAfterBytes), stream.GetWrittenBytes());
            Assert.True(result.CanWrite);
        }

        /// <summary>入力専用 Stream を出力に使う誤りを拒否する。</summary>
        [Fact]
        public void ReadOnlyOutputIsRejected()
        {
            using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);
            Assert.Throws<ArgumentException>(() => MidiSongFile.Write(CreateResult(), stream));
            Assert.True(stream.CanRead);
        }

        /// <summary>テストが所有する一時ディレクトリを解放する。</summary>
        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        private static MidiImportResult CreateResult(ChipKind chip = ChipKind.Nes)
            => MidiImporterTests.Import(MidiImporterTests.SimpleMidi(), new MidiImportOptions { Chip = chip });
    }
}
