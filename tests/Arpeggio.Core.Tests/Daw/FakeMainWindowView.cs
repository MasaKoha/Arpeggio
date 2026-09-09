using System;
using System.Threading.Tasks;
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
        /// <summary>合算後の警告件数。</summary>
        public int WarningCount { get; private set; }
        /// <summary>解析パネルのテキスト。</summary>
        public string AnalysisText { get; private set; } = string.Empty;
        /// <summary>解析パネルの実行状態。</summary>
        public bool IsAnalyzing { get; private set; }
        /// <summary>解析表示の更新回数。</summary>
        public int AnalysisDisplayCount { get; private set; }
        /// <summary>書き出しの通知。</summary>
        public string ExportStatus { get; private set; } = string.Empty;
        /// <summary>書き出しの実行状態。</summary>
        public bool IsExporting { get; private set; }
        /// <summary>書き出し表示の更新回数。</summary>
        public int ExportDisplayCount { get; private set; }
        /// <summary>「フォルダを開く」に渡された最後の書き出し先。</summary>
        public string? LastExportedPath { get; private set; }
        /// <summary>MIDI 表示の更新回数。</summary>
        public int MidiImportDisplayCount { get; private set; }
        /// <summary>ファイル監視の切替先。</summary>
        public string DocumentPath { get; private set; } = string.Empty;
        /// <summary>UI スレッド境界を経由した通知回数。</summary>
        public int DispatchCount { get; private set; }

        /// <summary>終了直前の通知待ちを検証する任意の UI ディスパッチ境界。</summary>
        internal Func<Action, Task>? Dispatch { get; set; }

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
        public void ShowStatus(string text, int warningCount)
        {
            Status = text;
            WarningCount = warningCount;
        }

        /// <summary>警告表示を記録する。</summary>
        public void ShowWarnings(string text) => Warnings = text;

        /// <summary>解析の表示結果を記録する。</summary>
        public void ShowAnalysis(string text, bool isRunning)
        {
            AnalysisText = text;
            IsAnalyzing = isRunning;
            AnalysisDisplayCount++;
        }

        /// <summary>書き出しの表示結果を記録する。</summary>
        public void ShowExportStatus(string text, bool isRunning, string? lastExportedPath)
        {
            ExportStatus = text;
            IsExporting = isRunning;
            LastExportedPath = lastExportedPath;
            ExportDisplayCount++;
        }

        /// <summary>MIDI の通知回数を記録する。</summary>
        public void ShowMidiImport() => MidiImportDisplayCount++;

        /// <summary>監視対象の切替を記録する。</summary>
        public void SwitchDocument(string path) => DocumentPath = path;

        /// <summary>Avalonia の代わりに通知をその場で実行する。</summary>
        public Task RunOnUiThreadAsync(Action action)
        {
            DispatchCount++;
            if (Dispatch is { } dispatch) { return dispatch(action); }
            action();
            return Task.CompletedTask;
        }
    }
}
