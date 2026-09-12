#if AVALON
using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Views;
using Avalon;
using Arpeggio.Daw.Presenters.PianoRoll;
using Arpeggio.Daw.Views.PianoRoll;

namespace Arpeggio.Daw.Diagnostics
{
    /// <summary>Debug 時の状態公開と診断ログを Avalon の寿命へ接続する。</summary>
    internal sealed class AvalonDawIntegration : IDisposable
    {
        private readonly AvalonHost host;
        private readonly DawDocument document;
        private readonly Func<MainWindowPresenter?> getPresenter;
        private readonly Func<MainWindow?> getWindow;
        private readonly List<string> stateKeys = new List<string>();
        private MainWindow? attachedWindow;
        private int registeredTrackCount;
        private string lastExportStatus = string.Empty;
        private bool isDisposed;

        /// <summary>起動順に依存しない取得関数と、文書の成功通知を登録する。</summary>
        public AvalonDawIntegration(AvalonHost host, DawDocument document,
            Func<MainWindowPresenter?> getPresenter, Func<MainWindow?> getWindow)
        {
            this.host = host;
            this.document = document;
            this.getPresenter = getPresenter;
            this.getWindow = getWindow;
            RegisterDocumentState();
            RegisterPianoRollState();
            RegisterViewportState();
            SynchronizeTrackKeys();
            document.Opened += OnDocumentOpened;
            document.Saved += OnDocumentSaved;
            host.Log.Write($"Avalon 観測開始: {document.Path}");
        }

        /// <summary>画面生成後に既存の書き出し表示通知を購読する。</summary>
        public void AttachWindow(MainWindow window)
        {
            if (ReferenceEquals(attachedWindow, window))
            {
                return;
            }
            if (attachedWindow is not null)
            {
                attachedWindow.ExportStatusChanged -= OnExportStatusChanged;
            }
            attachedWindow = window;
            attachedWindow.ExportStatusChanged += OnExportStatusChanged;
        }

        /// <summary>文書・画面の購読と状態参照を解除し、初期化失敗時にもホストを解放する。</summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            isDisposed = true;
            document.Opened -= OnDocumentOpened;
            document.Saved -= OnDocumentSaved;
            if (attachedWindow is not null)
            {
                attachedWindow.ExportStatusChanged -= OnExportStatusChanged;
                attachedWindow = null;
            }
            foreach (string key in stateKeys)
            {
                host.State.Unregister(key);
            }
            for (int trackIndex = 0; trackIndex < registeredTrackCount; trackIndex++)
            {
                host.State.Unregister(TrackKey(trackIndex));
            }
            host.Dispose();
        }

        private void RegisterDocumentState()
        {
            Register("daw.isReady", () => getPresenter() is not null && getWindow()?.IsVisible == true);
            Register("song.path", () => getPresenter()?.DocumentPath);
            Register("song.title", () => getPresenter()?.PianoRoll.Song.Title);
            Register("song.chip", () => getPresenter()?.PianoRoll.Song.Chip.ToString());
            Register("song.isDirty", () => getPresenter()?.IsDirty);
            Register("song.trackCount", () => getPresenter()?.PianoRoll.Song.Tracks.Count);
            Register("song.noteCount", () => getPresenter()?.PianoRoll.Song.Tracks.Sum(track => (long)track.Notes.Count));
            Register("transport.isPlaying", () => getPresenter()?.Transport.IsPlaying);
            Register("transport.positionTick", () => getPresenter()?.Transport.PositionTick);
        }

        private void RegisterPianoRollState()
        {
            Register("pianoRoll.selectedTrack", () => getPresenter()?.PianoRoll.SelectedTrack);
            Register("pianoRoll.selectedNoteCount", () => getPresenter()?.PianoRoll.SelectedNoteCount);
            Register("pianoRoll.selectedTicks", () => getPresenter()?.PianoRoll.SelectedTicks);
            Register("pianoRoll.snap", () => getPresenter()?.PianoRoll.SnapTicks);
        }

        private void RegisterViewportState()
        {
            Register("pianoRoll.pixelsPerTick", () => getWindow()?.PianoRollPixelsPerTick);
            Register("pianoRoll.noteHeight", () => PianoRollControl.NoteHeight);
            Register("pianoRoll.topPitch", () => PianoRollPresenter.MaximumMidiNote);
            Register("pianoRoll.originX", () => getWindow()?.PianoRollOrigin?.X);
            Register("pianoRoll.originY", () => getWindow()?.PianoRollOrigin?.Y);
            Register("pianoRoll.scrollOffsetX", () => getWindow()?.PianoRollScrollOffset.X);
            Register("pianoRoll.scrollOffsetY", () => getWindow()?.PianoRollScrollOffset.Y);
            Register("pianoRoll.viewportWidth", () => getWindow()?.PianoRollViewportSize.Width);
            Register("pianoRoll.viewportHeight", () => getWindow()?.PianoRollViewportSize.Height);
        }

        private void Register(string key, Func<object?> reader)
        {
            host.State.Register(key, reader);
            stateKeys.Add(key);
        }

        private void SynchronizeTrackKeys()
        {
            // 文書切替の通知で登録だけを更新し、観測の取得関数には副作用を持たせない。
            int trackCount = document.Song.Tracks.Count;
            for (int trackIndex = trackCount; trackIndex < registeredTrackCount; trackIndex++)
            {
                host.State.Unregister(TrackKey(trackIndex));
            }
            for (int trackIndex = registeredTrackCount; trackIndex < trackCount; trackIndex++)
            {
                int capturedTrack = trackIndex;
                host.State.Register(TrackKey(capturedTrack), () => ReadTrackNoteCount(capturedTrack));
            }
            registeredTrackCount = trackCount;
        }

        private int? ReadTrackNoteCount(int trackIndex)
        {
            MainWindowPresenter? presenter = getPresenter();
            if (presenter is null || trackIndex >= presenter.PianoRoll.Song.Tracks.Count)
            {
                return null;
            }
            return presenter.PianoRoll.Song.Tracks[trackIndex].Notes.Count;
        }

        private static string TrackKey(int trackIndex) => $"track.{trackIndex}.noteCount";

        private void OnDocumentOpened(string path)
        {
            SynchronizeTrackKeys();
            host.Log.Write($"ファイルを開きました: {path}");
        }

        private void OnDocumentSaved(string path) => host.Log.Write($"ファイルを保存しました: {path}");

        private void OnExportStatusChanged(string text)
        {
            if (text == lastExportStatus)
            {
                return;
            }
            lastExportStatus = text;
            host.Log.Write(text);
        }
    }
}
#endif
