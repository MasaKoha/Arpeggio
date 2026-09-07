using Arpeggio.Core.Document;
using Arpeggio.Daw.Presenters;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>Avalonia を起動せず、Presenter の表示結果を記録する。</summary>
    internal sealed class FakeMainWindowView : IMainWindowView
    {
        /// <summary>最後に表示したソング。</summary>
        public Song? Song { get; private set; }
        /// <summary>ソングの表示更新回数。</summary>
        public int SongDisplayCount { get; private set; }
        /// <summary>表示中の選択トラック。</summary>
        public int SelectedTrack { get; private set; }
        /// <summary>表示中の選択ノート。</summary>
        public int? SelectedTick { get; private set; }
        /// <summary>表示中の再生状態。</summary>
        public bool IsPlaying { get; private set; }
        /// <summary>表示中のループ状態。</summary>
        public bool IsLooping { get; private set; }
        /// <summary>表示中のテンポ。</summary>
        public int TempoBpm { get; private set; }
        /// <summary>表示中の tick 位置。</summary>
        public double PositionTick { get; private set; }
        /// <summary>表示中の位置文字列。</summary>
        public string Position { get; private set; } = string.Empty;
        /// <summary>表示中のステータス。</summary>
        public string Status { get; private set; } = string.Empty;
        /// <summary>表示中の警告一覧。</summary>
        public string Warnings { get; private set; } = string.Empty;

        /// <summary>編集表示を記録する。</summary>
        public void ShowSong(Song song, int selectedTrack, int? selectedTick)
        {
            Song = song;
            SelectedTrack = selectedTrack;
            SelectedTick = selectedTick;
            SongDisplayCount++;
        }

        /// <summary>再生表示を記録する。</summary>
        public void ShowTransport(bool isPlaying, bool isLooping, int tempoBpm, int lengthTicks,
            double positionTick, string position)
        {
            IsPlaying = isPlaying;
            IsLooping = isLooping;
            TempoBpm = tempoBpm;
            PositionTick = positionTick;
            Position = position;
        }

        /// <summary>通知表示を記録する。</summary>
        public void ShowStatus(string text, int warningCount) => Status = text;

        /// <summary>警告表示を記録する。</summary>
        public void ShowWarnings(string text) => Warnings = text;
    }
}
