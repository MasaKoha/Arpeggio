using System;
using System.IO;
using System.Threading;
using Arpeggio.Core.Document;
using Arpeggio.Formats.Export.Control;
using Arpeggio.Formats.Export.GameBoy;
using Arpeggio.Formats.Export.Nes;
using Arpeggio.Formats.Export.Nsf;
using Arpeggio.Formats.Export.Vgm;

namespace Arpeggio.Formats.Export
{
    /// <summary>Song の独立変換と、検証済み NSF／VGM の安全なファイル保存を仲介する。</summary>
    public static class ChipExportService
    {
        /// <summary>元 Song とファイルを変更せず、strict でも全変換と予定サイズの検証を完了する。</summary>
        public static ChipExportPlan Prepare(Song song, ChipExportOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var conversionOptions = new ChipExportOptions
            {
                Format = options.Format, Loops = options.Loops,
                Author = options.Author, Copyright = options.Copyright
            };
            ControlTimelineResult result = ControlTimeline.Create(song, conversionOptions);
            Action<Stream>? write = null;
            if (result.Timeline is ControlTimeline timeline)
            {
                write = options.Format == ConversionFormat.Nsf
                    ? PrepareNsf(timeline, options, result.Report)
                    : PrepareVgm(timeline, options, result.Report);
            }
            return new ChipExportPlan(write, new ConversionReport(result.Report, options.Strict));
        }

        /// <summary>隣接一時ファイルへ保存後に移動する。既定は上書き不可。入力パスが分かる場合は sourcePath を渡す。同一パスは常に拒否し、I/O・競合・キャンセルは伝播する。</summary>
        public static bool Write(ChipExportPlan plan, string path, bool overwrite = false,
            CancellationToken cancellationToken = default, string? sourcePath = null)
        {
            ArgumentNullException.ThrowIfNull(plan);
            string destination = ValidateDestination(path, sourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            if (!plan.CanWrite)
            {
                return false;
            }
            if (!overwrite && (File.Exists(destination) || Directory.Exists(destination)))
            {
                throw new IOException("保存先が既に存在します。上書きには overwrite の指定が必要です。");
            }
            return SaveFile(plan, destination, overwrite, cancellationToken);
        }

        /// <summary>確定済み内容を現在位置へ書く。拒否時は false。Stream は閉じず、I/O・途中キャンセルによる部分書き込みは巻き戻せない。</summary>
        public static bool Write(ChipExportPlan plan, Stream destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
            {
                throw new ArgumentException("書き込み可能な Stream を指定してください。", nameof(destination));
            }
            cancellationToken.ThrowIfCancellationRequested();
            bool written = plan.WriteTo(destination);
            cancellationToken.ThrowIfCancellationRequested();
            return written;
        }

        private static string ValidateDestination(string path, string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("保存先を指定してください。", nameof(path));
            }
            string destination = Path.GetFullPath(path);
            if (sourcePath != null && string.Equals(destination, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("入力と同じパスには書き出せません。", nameof(path));
            }
            return destination;
        }

        private static Action<Stream>? PrepareNsf(ControlTimeline timeline, ChipExportOptions options, ConversionReport report)
        {
            NsfFrameTimeline? frames = NsfFrameCompiler.Compile(timeline, report);
            if (frames is null)
            {
                return null;
            }
            NsfEncodedData? data = NsfDataEncoder.Encode(frames, report);
            return data is null ? null : NsfWriter.Prepare(data, timeline.Title, report, options.Author, options.Copyright);
        }

        private static Action<Stream>? PrepareVgm(ControlTimeline timeline, ChipExportOptions options, ConversionReport report)
        {
            RegisterTimeline? registers = timeline.Chip == ChipKind.Nes
                ? NesRegisterCompiler.Compile(timeline, report)
                : GameBoyRegisterCompiler.Compile(timeline, report);
            return registers is null ? null : VgmWriter.PrepareWrite(registers, timeline.Title, options.Author, report);
        }

        private static bool SaveFile(ChipExportPlan plan, string destination, bool overwrite, CancellationToken cancellationToken)
        {
            string temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool created = false;
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    if (!Write(plan, stream, cancellationToken))
                    {
                        return false;
                    }
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!plan.CanWrite)
                {
                    return false;
                }
                File.Move(temporaryPath, destination, overwrite);
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
