using System;
using System.IO;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Daw.Audio;
using Arpeggio.Daw.Editing;
using Arpeggio.Daw.Presenters;

namespace Arpeggio.Core.Tests.Daw
{
    /// <summary>正本と作業ファイルを各テストに隔離し、所有順に解放する。</summary>
    internal sealed class DawPresenterFixture : IDisposable
    {
        private const int InitialTempo = 150;
        private const int SongLength = 768;
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "arpeggio-daw-tests-" + Guid.NewGuid().ToString("N"));

        /// <summary>指定チップの空ソングで Presenter を明示的に組み立てる。省略時は NES。</summary>
        public DawPresenterFixture(ChipKind chip = ChipKind.Nes)
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "song.arpeggio.json");
            EditSession session = new EditSession();
            session.New(Path, chip, InitialTempo, SongLength);
            Presenter = new MainWindowPresenter(View, Document, new PlaybackEngine(Audio));
            try
            {
                Presenter.Open(Path);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>外部編集対象の正本パス。</summary>
        public string Path { get; }
        /// <summary>画面表示のテスト境界。</summary>
        public FakeMainWindowView View { get; } = new FakeMainWindowView();
        /// <summary>オーディオ要求のテスト境界。</summary>
        public FakeAudioOutput Audio { get; } = new FakeAudioOutput();
        /// <summary>履歴・公開ソングを保持する作業ドキュメント。</summary>
        public DawDocument Document { get; } = new DawDocument();
        /// <summary>テスト対象。</summary>
        public MainWindowPresenter Presenter { get; }

        /// <summary>CLI と同じ編集セッションから正本を更新する。</summary>
        public void AddExternalNote(int tick, int midiNote)
        {
            EditSession session = new EditSession();
            session.Open(Path);
            session.Notes.Add(0, new Note { Tick = tick, MidiNote = midiNote });
        }

        /// <summary>Presenter の所有リソースと正本を解放する。</summary>
        public void Dispose()
        {
            Presenter.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
