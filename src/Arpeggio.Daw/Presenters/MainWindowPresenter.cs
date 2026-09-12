using System;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Audio.Sfx;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Presenters.Sfx;
using Arpeggio.Daw.Presenters.Analysis;
using Arpeggio.Daw.Presenters.Export;
using Arpeggio.Daw.Presenters.Instrument;
using Arpeggio.Daw.Presenters.Midi;
using Arpeggio.Daw.Presenters.PianoRoll;
using Arpeggio.Daw.Presenters.Transport;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>画面全体の編集・保存・再生・外部変更の状態遷移を統括する。</summary>
    public sealed class MainWindowPresenter : IDisposable
    {
        private readonly IMainWindowView view;
        private readonly DawDocument document;
        private readonly PlaybackEngine playback;
        private readonly CompositeDisposable sfxSubscriptions = new CompositeDisposable();
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
            SnesEcho = new SnesEchoPresenter(document, PianoRoll, Transport);
            Notes = new NotePanelPresenter(document, PianoRoll, Refresh);
            Analysis = new AnalysisPresenter(document, view, RefreshStatus);
            Export = new ExportPresenter(document, view, RefreshStatus);
            SfxCreation = new SfxCreationPresenter(document, PrepareDocumentSwitch, Open);
            MidiImport = new MidiImportPresenter(document, view, OpenImportedSong);
            SfxEditor = new SfxEditorPresenter(document);
            SfxPreview = new SfxPreviewPlayer(playback);
            sfxSubscriptions.Add(SfxEditor.PreviewRequests.Subscribe(SfxPreview.Play));
            sfxSubscriptions.Add(SfxEditor.PreviewStops.Subscribe(_ => SfxPreview.Stop()));
            sfxSubscriptions.Add(SfxEditor.OpenRequests.Subscribe(OpenImportedSong));
            sfxSubscriptions.Add(SfxEditor.Commits.Subscribe(_ =>
            {
                if (!SfxEditor.Model.IsNewCandidate)
                {
                    playback.Load(document.Song);
                    Refresh();
                }
            }));
        }
        /// <summary>ノート操作の状態機械。</summary>
        public PianoRollPresenter PianoRoll { get; }
        /// <summary>音色編集。</summary>
        public InstrumentPanelPresenter Instruments { get; }
        /// <summary>再生と構造編集。</summary>
        public TransportPresenter Transport { get; }
        /// <summary>ソング単位の SNES エコー編集。</summary>
        public SnesEchoPresenter SnesEcho { get; }
        /// <summary>選択ノートの効果編集。</summary>
        public NotePanelPresenter Notes { get; }
        /// <summary>非同期の音声解析。</summary>
        public AnalysisPresenter Analysis { get; }
        /// <summary>非同期の音声書き出しとチップ変換の診断・保存。</summary>
        public ExportPresenter Export { get; }
        /// <summary>効果音プリセットの新規作成。</summary>
        public SfxCreationPresenter SfxCreation { get; }
        /// <summary>MIDI 候補の確認・新規保存と保護付き Open。</summary>
        public MidiImportPresenter MidiImport { get; }
        /// <summary>候補と編集中ファイルのパラメータ SFX 編集。</summary>
        public SfxEditorPresenter SfxEditor { get; }
        /// <summary>通常再生と出力を共有するSFX専用試聴。</summary>
        public SfxPreviewPlayer SfxPreview { get; }
        /// <summary>現在の文書の保存先。</summary>
        public string DocumentPath => document.Path;
        /// <summary>外部変更の確認待ちか。</summary>
        public bool HasPendingExternalChange { get; private set; }
        /// <summary>未保存の編集があるか。</summary>
        public bool IsDirty => document.IsDirty;

        /// <summary>初期ファイルを読み、合成器を準備する。</summary>
        public void Open(string path)
        {
            SfxEditor.Stop();
            PianoRoll.EndDrag();
            document.Open(path);
            SfxEditor.FollowDocument();
            playback.Load(document.Song);
            Instruments.ResetSelection();
            PianoRoll.SelectTrack(0);
            HasPendingExternalChange = false;
            view.SwitchDocument(document.Path);
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
            SfxEditor.Commit();
            if (SfxEditor.Model.HasGesture) { return; }
            PianoRoll.EndDrag();
            if (HasPendingExternalChange || document.HasExternalChange())
            {
                HasPendingExternalChange = true;
                SfxEditor.ExternalChange();
                message = "外部変更を再読み込みしてから保存してください。";
                RefreshStatus();
                return;
            }
            document.Save();
            Refresh();
        }
        /// <summary>現在の再生カーソルへノートを貼り付ける。</summary>
        public void PasteNotesAtCursor() => PianoRoll.Paste(playback.PositionTick);
        /// <summary>一操作戻す。</summary>
        public void Undo()
        {
            SfxEditor.Cancel();
            PianoRoll.ClearSelection();
            if (document.Session.History.UndoCount == 0) { return; }
            Transport.ChangeStructure(() => document.Session.Undo());
        }
        /// <summary>一操作やり直す。</summary>
        public void Redo()
        {
            SfxEditor.Cancel();
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
            SfxEditor.ExternalChange();
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
            SfxPreview.Poll();
            RefreshTransport();
            view.ShowStatus(statusText, TotalWarningCount);
        }
        /// <summary>警告一覧を表示する。</summary>
        public void ShowWarnings() => view.ShowWarnings(playback.GetWarningsText() +
            (Analysis.WarningCount > 0 ? "\n\n解析:\n" + Analysis.ResultText : string.Empty));
        /// <summary>編集通知で表示内容を更新する。</summary>
        public void Refresh()
        {
            SfxEditor.RefreshDocument();
            Analysis.RefreshValidity();
            view.ShowSong(document.Song, PianoRoll.SelectedTrack, PianoRoll.SelectedTick);
            RefreshTransport();
            RefreshStatus();
        }
        /// <summary>音声を止めてから作業ファイルを解放する。</summary>
        public void Dispose()
        {
            if (isDisposed) { return; }
            isDisposed = true;
            SfxEditor.Dispose();
            sfxSubscriptions.Dispose();
            SfxPreview.Dispose();
            Analysis.Dispose();
            Export.Dispose();
            MidiImport.Dispose();
            playback.Dispose();
            document.Dispose();
        }
        private void OpenImportedSong(string path)
        {
            PianoRoll.EndDrag();
            if (HasPendingExternalChange || document.HasExternalChange())
            {
                HasPendingExternalChange = true;
                RefreshStatus();
                throw new InvalidOperationException("現在の文書の外部変更を再読み込みしてから開いてください。");
            }
            if (document.IsDirty)
            {
                throw new InvalidOperationException("現在の編集を保存してから開いてください。");
            }
            Open(path);
        }
        private int ResolveInstrument() => Instruments.ResolveInstrumentId();
        private int TotalWarningCount => (int)Math.Min(int.MaxValue, (long)playback.WarningCount + Analysis.WarningCount);
        private void PrepareDocumentSwitch()
        {
            PianoRoll.EndDrag();
            if (!document.IsDirty) { return; }
            Save();
            if (document.IsDirty)
            {
                throw new InvalidOperationException("現在の編集を保存してから SFX を作成してください。");
            }
        }
        private void Reload()
        {
            PianoRoll.ClearSelection();
            bool resume = playback.IsPlaying;
            playback.Stop();
            document.Open(document.Path);
            if (!SfxEditor.Model.IsNewCandidate) { SfxEditor.FollowDocument(); }
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
            string selection = PianoRoll.Selection.Count > 0 ? $"  {PianoRoll.Selection.Count} 音選択" : string.Empty;
            statusText = $"{document.Path}  |  {document.Song.Chip}  {(document.IsDirty ? "● 未保存" : "保存済み")}{external}{selection}  {message}  {PianoRoll.StatusText}  {Export.StatusText}";
            view.ShowStatus(statusText, TotalWarningCount);
        }
    }
}
