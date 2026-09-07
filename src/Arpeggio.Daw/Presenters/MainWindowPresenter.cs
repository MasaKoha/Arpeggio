using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>画面全体の編集・保存・再生・外部変更の状態遷移を統括する。</summary>
    public sealed class MainWindowPresenter : IDisposable
    {
        private readonly IMainWindowView view;
        private readonly DawDocument document;
        private readonly PlaybackEngine playback;
        private string message = string.Empty;
        private string statusText = string.Empty;
        private bool isDisposed;
        private bool displayedPlaying;
        private bool displayedLooping;
        private int displayedTempo;
        private int displayedLength;
        private double displayedPosition = -1;

        /// <summary>所有リソースと画面境界を明示的に受け取る。</summary>
        public MainWindowPresenter(IMainWindowView view, DawDocument document, PlaybackEngine playback)
        {
            this.view = view;
            this.document = document;
            this.playback = playback;
            PianoRoll = new PianoRollPresenter(document, Refresh, ResolveInstrument);
            Instruments = new InstrumentPanelPresenter(document, PianoRoll, Refresh);
            Transport = new TransportPresenter(document, playback, Refresh);
        }
        /// <summary>ノート操作の状態機械。</summary>
        public PianoRollPresenter PianoRoll { get; }
        /// <summary>音色編集。</summary>
        public InstrumentPanelPresenter Instruments { get; }
        /// <summary>再生と構造編集。</summary>
        public TransportPresenter Transport { get; }
        /// <summary>外部変更の確認待ちか。</summary>
        public bool HasPendingExternalChange { get; private set; }
        /// <summary>未保存の編集があるか。</summary>
        public bool IsDirty => document.IsDirty;

        /// <summary>初期ファイルを読み、合成器を準備する。</summary>
        public void Open(string path)
        {
            document.Open(path);
            playback.Load(document.Song);
            PianoRoll.SelectTrack(0);
            HasPendingExternalChange = false;
            Refresh();
        }
        /// <summary>入力エラーを非モーダルなステータスへ変換する。</summary>
        public void Execute(Action action)
        {
            if (isDisposed) { return; }
            try { message = string.Empty; action(); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                IOException or UnauthorizedAccessException or SongValidationException or FormatException or OverflowException or DllNotFoundException or
                EntryPointNotFoundException or BadImageFormatException)
            {
                message = exception.Message;
                RefreshStatus();
            }
        }
        /// <summary>正本を保存する。確認待ちの外部変更は上書きしない。</summary>
        public void Save()
        {
            PianoRoll.EndDrag();
            if (HasPendingExternalChange || document.HasExternalChange())
            {
                HasPendingExternalChange = true;
                message = "外部変更を再読み込みしてから保存してください。";
                RefreshStatus();
                return;
            }
            document.Save();
            Refresh();
        }
        /// <summary>一操作戻す。</summary>
        public void Undo()
        {
            PianoRoll.ClearSelection();
            if (document.Session.History.UndoCount == 0) { return; }
            Transport.ChangeStructure(() => document.Session.Undo());
        }
        /// <summary>一操作やり直す。</summary>
        public void Redo()
        {
            PianoRoll.ClearSelection();
            if (document.Session.History.RedoCount == 0) { return; }
            Transport.ChangeStructure(() => document.Session.Redo());
        }
        /// <summary>トラックのミュートを一操作として記録する。</summary>
        public void ToggleMute(int trackIndex)
        {
            PianoRoll.EndDrag();
            BatchOperationApplier.Apply(document.Session, new[] { new BatchOperation
            {
                Kind = BatchOperationKind.SetTrackMuted, Track = trackIndex,
                Muted = !document.Song.Tracks[trackIndex].Muted
            } });
            Refresh();
        }
        /// <summary>自分の保存を除外し、編集中なら確認待ちにする。</summary>
        public void ExternalFileChanged()
        {
            if (isDisposed || !document.HasExternalChange()) { return; }
            if (document.IsDirty)
            {
                HasPendingExternalChange = true;
                RefreshStatus();
                return;
            }
            Reload();
        }
        /// <summary>R による明示確認後、正本を再読み込みする。</summary>
        public void ConfirmReload()
        {
            if (HasPendingExternalChange) { Reload(); }
        }
        /// <summary>描画タイマーは合成せず、公開された再生位置だけを読む。</summary>
        public void Poll()
        {
            if (isDisposed) { return; }
            playback.Poll();
            RefreshTransport();
            view.ShowStatus(statusText, playback.WarningCount);
        }
        /// <summary>警告一覧を表示する。</summary>
        public void ShowWarnings() => view.ShowWarnings(playback.GetWarningsText());
        /// <summary>編集通知で表示内容を更新する。</summary>
        public void Refresh()
        {
            view.ShowSong(document.Song, PianoRoll.SelectedTrack, PianoRoll.SelectedTick);
            RefreshTransport();
            RefreshStatus();
        }
        /// <summary>音声を止めてから作業ファイルを解放する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            playback.Dispose();
            document.Dispose();
        }
        private int ResolveInstrument() => Instruments.ResolveInstrumentId();
        private void Reload()
        {
            PianoRoll.ClearSelection();
            bool resume = playback.IsPlaying;
            playback.Stop();
            document.Open(document.Path);
            playback.Load(document.Song);
            HasPendingExternalChange = false;
            PianoRoll.SelectTrack(Math.Min(PianoRoll.SelectedTrack, document.Song.Tracks.Count - 1));
            if (resume) { playback.Play(); }
            Refresh();
        }
        private void RefreshTransport()
        {
            double position = playback.PositionTick;
            if (displayedPlaying == playback.IsPlaying && displayedLooping == playback.IsLooping &&
                displayedTempo == document.Song.TempoBpm && displayedLength == document.Song.LengthTicks && displayedPosition == position)
            {
                return;
            }
            displayedPlaying = playback.IsPlaying;
            displayedLooping = playback.IsLooping;
            displayedTempo = document.Song.TempoBpm;
            displayedLength = document.Song.LengthTicks;
            displayedPosition = position;
            view.ShowTransport(displayedPlaying, displayedLooping, displayedTempo,
                displayedLength, position, TransportPresenter.FormatPosition(position));
        }
        private void RefreshStatus()
        {
            string external = HasPendingExternalChange ? "  外部で変更されました。再読み込み（R）" : string.Empty;
            statusText = $"{document.Path}  |  {document.Song.Chip}  {(document.IsDirty ? "● 未保存" : "保存済み")}{external}  {message}";
            view.ShowStatus(statusText, playback.WarningCount);
        }
    }
}
