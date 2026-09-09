using System;
using System.Threading.Tasks;
using Arpeggio.Core.Document;

namespace Arpeggio.Daw.Presenters
{
    /// <summary>Avalonia に依存しない画面表示の境界。</summary>
    public interface IMainWindowView
    {
        /// <summary>編集対象と選択を更新する。</summary>
        void ShowSong(Song song, int selectedTrack, int? selectedTick);
        /// <summary>再生状態と位置を更新する。</summary>
        void ShowTransport(bool isPlaying, bool isLooping, int tempoBpm, int lengthTicks, double positionTick, string position);
        /// <summary>保存状態・外部変更・エラーを表示する。</summary>
        void ShowStatus(string text, int warningCount);
        /// <summary>警告を非モーダルなテキストとして表示する。</summary>
        void ShowWarnings(string text);
        /// <summary>解析結果と操作可否を表示する。</summary>
        void ShowAnalysis(string text, bool isRunning);
        /// <summary>書き出し通知と操作可否を表示する。</summary>
        void ShowExportStatus(string text, bool isRunning, string? lastExportedPath);
        /// <summary>MIDI 候補・操作状態の表示を更新する。</summary>
        void ShowMidiImport();
        /// <summary>ファイル監視と保存先入力を新しい文書へ切り替える。</summary>
        void SwitchDocument(string path);
        /// <summary>非同期処理の通知を UI スレッドで実行し、完了を待つ。</summary>
        Task RunOnUiThreadAsync(Action action);
    }
}
