using System;
using System.IO;
using System.Text;
using System.Threading;
using Arpeggio.Formats.Midi.Import;

namespace Arpeggio.Formats.Midi
{
    /// <summary>検証済み MIDI 変換結果を、既存ファイルを上書きせずに保存する。</summary>
    public static class MidiSongFile
    {
        private const int WriteBufferCharacters = 4096;

        /// <summary>隣接一時ファイルから新規移動で確定する。dry-run は存在確認だけを行う。I/O・競合・キャンセルは例外を伝播する。</summary>
        public static MidiSongFileResult Write(MidiImportResult result, string path, bool dryRun = false,
            CancellationToken cancellationToken = default, string? sourcePath = null)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("保存先を指定してください。", nameof(path));
            }
            string destination = Path.GetFullPath(path);
            if (sourcePath != null && string.Equals(destination, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("MIDI 入力と同じパスには保存できません。", nameof(path));
            }
            cancellationToken.ThrowIfCancellationRequested();
            bool destinationExists = File.Exists(destination) || Directory.Exists(destination);
            if (dryRun || !result.CanWrite)
            {
                return new MidiSongFileResult(false, destinationExists, result.Report);
            }
            if (destinationExists)
            {
                throw new IOException("保存先が既に存在します。別のパスを指定してください。");
            }
            bool written = SaveNewFile(result, destination, cancellationToken);
            return new MidiSongFileResult(written, false, result.Report);
        }

        /// <summary>呼び出し元所有の Stream へ確定済み JSON を書く。変換拒否時は false。I/O・途中キャンセルによる部分書き込みは巻き戻せない。</summary>
        public static bool Write(MidiImportResult result, Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanWrite)
            {
                throw new ArgumentException("書き込み可能な Stream が必要です。", nameof(stream));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.CanWrite)
            {
                return false;
            }
            string json = result.Json!;
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), WriteBufferCharacters, leaveOpen: true);
            for (int position = 0; position < json.Length; position += WriteBufferCharacters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(WriteBufferCharacters, json.Length - position);
                writer.Write(json.AsSpan(position, count));
            }
            writer.Flush();
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        private static bool SaveNewFile(MidiImportResult result, string destination, CancellationToken cancellationToken)
        {
            string temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool created = false;
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    if (!Write(result, stream, cancellationToken))
                    {
                        return false;
                    }
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.CanWrite)
                {
                    return false;
                }
                // 確認後に保存先が作られても上書きしない。候補を再変換せず、検証した JSON を移動する。
                File.Move(temporaryPath, destination, false);
                return true;
            }
            finally
            {
                if (created && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
