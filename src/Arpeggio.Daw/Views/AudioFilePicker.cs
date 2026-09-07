using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arpeggio.Core.Instruments;
using Arpeggio.Daw.Presenters;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Arpeggio.Daw.Views
{
    /// <summary>OS ファイル選択の寿命と、選択待ち中の文書変更を扱う。</summary>
    internal sealed class AudioFilePicker : IDisposable
    {
        private readonly Window window;
        private readonly FilePickerFileType wavType = new FilePickerFileType("WAV 音声")
        {
            Patterns = new[] { "*.wav" }, MimeTypes = new[] { "audio/wav" },
            AppleUniformTypeIdentifiers = new[] { "com.microsoft.waveform-audio" }
        };
        private readonly FilePickerFileType oggType = new FilePickerFileType("OGG Vorbis 音声")
        {
            Patterns = new[] { "*.ogg" }, MimeTypes = new[] { "audio/ogg" }
        };
        private bool isPicking;
        private bool isDisposed;

        internal AudioFilePicker(Window window) => this.window = window;

        internal async Task ExportAsync(MainWindowPresenter presenter)
        {
            if (isDisposed || isPicking || presenter.Export.IsRunning) { return; }
            isPicking = true;
            try
            {
                string sourcePath = presenter.DocumentPath;
                using IStorageFolder? directory = await window.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(sourcePath)!);
                if (isDisposed) { return; }
                using IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "WAV / OGG を書き出す", SuggestedStartLocation = directory,
                    SuggestedFileName = GetSongBaseName(sourcePath) + ".wav",
                    DefaultExtension = "wav", FileTypeChoices = new[] { wavType, oggType }, ShowOverwritePrompt = true
                });
                if (isDisposed || file == null) { return; }
                if (sourcePath != presenter.DocumentPath)
                {
                    throw new InvalidOperationException("文書が切り替わりました。書き出し先を選び直してください。");
                }
                await presenter.Export.RunAsync(RequireLocalPath(file));
            }
            catch (Exception exception)
            {
                if (!isDisposed) { presenter.Execute(() => throw new InvalidOperationException($"保存先の選択に失敗しました: {exception.Message}", exception)); }
            }
            finally { isPicking = false; }
        }

        internal async Task ImportAsync(MainWindowPresenter presenter, string rootNote, bool loop)
        {
            if (isDisposed || isPicking) { return; }
            isPicking = true;
            try
            {
                Instrument? instrument = presenter.Instruments.CurrentInstrument;
                IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "SNES 音色へ WAV を取り込む", AllowMultiple = false, FileTypeFilter = new[] { wavType }
                });
                try
                {
                    if (isDisposed || files.Count == 0) { return; }
                    if (!ReferenceEquals(instrument, presenter.Instruments.CurrentInstrument))
                    {
                        throw new InvalidOperationException("選択音色が変わりました。WAV を選び直してください。");
                    }
                    presenter.Execute(() => presenter.Instruments.ImportWav(RequireLocalPath(files[0]), rootNote, loop));
                }
                finally
                {
                    foreach (IStorageFile file in files) { file.Dispose(); }
                }
            }
            catch (Exception exception)
            {
                if (!isDisposed) { presenter.Execute(() => throw new InvalidOperationException($"WAV の選択に失敗しました: {exception.Message}", exception)); }
            }
            finally { isPicking = false; }
        }

        /// <summary>閉じた画面への選択結果の適用を抑止する。</summary>
        public void Dispose() => isDisposed = true;

        private static string RequireLocalPath(IStorageFile file) => file.TryGetLocalPath()
            ?? throw new InvalidOperationException("ローカルファイルを選択してください。");

        /// <summary>`.arpeggio.json` の二重拡張子を外して書き出しの既定名を作る。</summary>
        private static string GetSongBaseName(string sourcePath)
        {
            const string SongSuffix = ".arpeggio";
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            if (baseName.EndsWith(SongSuffix, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName.Substring(0, baseName.Length - SongSuffix.Length);
            }
            return baseName;
        }
    }
}
