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
    }
}
